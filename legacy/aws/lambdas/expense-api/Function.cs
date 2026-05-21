using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ExpenseApi;

public sealed class Function
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IAmazonDynamoDB dynamoDb;
    private readonly IAmazonS3 s3;
    private readonly string tableName;
    private readonly string bucketName;
    private readonly string appOrigin;

    public Function()
        : this(new AmazonDynamoDBClient(), new AmazonS3Client())
    {
    }

    public Function(IAmazonDynamoDB dynamoDb, IAmazonS3 s3)
    {
        this.dynamoDb = dynamoDb;
        this.s3 = s3;
        tableName = Environment.GetEnvironmentVariable("EXPENSE_TABLE_NAME") ?? "";
        bucketName = Environment.GetEnvironmentVariable("RECEIPTS_BUCKET_NAME") ?? "";
        appOrigin = Environment.GetEnvironmentVariable("APP_ORIGIN") ?? "*";
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context)
    {
        try
        {
            var method = request.RequestContext.Http.Method;
            var path = request.RawPath;
            var user = GetUser(request);

            if (method == "POST" && path == "/expenses/upload-url")
            {
                var body = ParseBody<UploadUrlRequest>(request);
                var key = $"receipts/{user.Sub}/{Guid.NewGuid()}-{SafeFileName(body.FileName)}";
                var uploadUrl = s3.GetPreSignedURL(new GetPreSignedUrlRequest
                {
                    BucketName = bucketName,
                    Key = key,
                    Verb = HttpVerb.PUT,
                    ContentType = body.ContentType,
                    Expires = DateTime.UtcNow.AddMinutes(5)
                });

                return Json(new UploadUrlResponse(
                    "aws",
                    key,
                    uploadUrl,
                    "PUT",
                    new Dictionary<string, string> { ["Content-Type"] = body.ContentType }
                ));
            }

            if (method == "POST" && path == "/expenses")
            {
                var body = ParseBody<CreateExpenseRequest>(request);
                var now = DateTime.UtcNow.ToString("O");
                var expense = new ExpenseRequest(
                    Guid.NewGuid().ToString(),
                    user.Sub,
                    user.Email,
                    user.Name,
                    body.Amount,
                    "EUR",
                    body.Category,
                    body.Description,
                    body.ReceiptS3Key,
                    body.ReceiptFileName,
                    body.ReceiptContentType,
                    null,
                    null,
                    "PENDING",
                    null,
                    null,
                    now,
                    now
                );

                await dynamoDb.PutItemAsync(new PutItemRequest
                {
                    TableName = tableName,
                    Item = ToItem(expense),
                    ConditionExpression = "attribute_not_exists(requestId)"
                });

                return Json(await WithReceiptUrl(expense), HttpStatusCode.Created);
            }

            if (method == "GET" && path == "/expenses")
            {
                var query = request.QueryStringParameters ?? new Dictionary<string, string>();
                var adminMode = query.TryGetValue("admin", out var admin) && admin == "true";
                var status = query.TryGetValue("status", out var requestedStatus)
                    ? requestedStatus
                    : "PENDING";

                if (adminMode)
                {
                    AssertAdmin(user);
                    var result = await dynamoDb.QueryAsync(new QueryRequest
                    {
                        TableName = tableName,
                        IndexName = "status-createdAt-index",
                        KeyConditionExpression = "#status = :status",
                        ExpressionAttributeNames = new Dictionary<string, string>
                        {
                            ["#status"] = "status"
                        },
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":status"] = new(status)
                        },
                        ScanIndexForward = false
                    });

                    return Json(await WithReceiptUrls(result.Items.Select(FromItem)));
                }

                var userResult = await dynamoDb.QueryAsync(new QueryRequest
                {
                    TableName = tableName,
                    IndexName = "employeeId-createdAt-index",
                    KeyConditionExpression = "employeeId = :employeeId",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":employeeId"] = new(user.Sub)
                    },
                    ScanIndexForward = false
                });

                return Json(await WithReceiptUrls(userResult.Items.Select(FromItem)));
            }

            if (method == "PATCH" && path.StartsWith("/admin/expenses/", StringComparison.Ordinal))
            {
                AssertAdmin(user);
                var requestId = path.Split('/').Last();
                var body = ParseBody<DecisionRequest>(request);

                if (body.Status is not ("APPROVED" or "REJECTED"))
                {
                    return Json(new ErrorResponse("Statut invalide"), HttpStatusCode.BadRequest);
                }

                var existing = await dynamoDb.GetItemAsync(new GetItemRequest
                {
                    TableName = tableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["requestId"] = new(requestId)
                    }
                });

                if (existing.Item.Count == 0)
                {
                    return Json(new ErrorResponse("Demande introuvable"), HttpStatusCode.NotFound);
                }

                var update = await dynamoDb.UpdateItemAsync(new UpdateItemRequest
                {
                    TableName = tableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["requestId"] = new(requestId)
                    },
                    UpdateExpression =
                        "SET #status = :status, adminComment = :adminComment, reviewedBy = :reviewedBy, updatedAt = :updatedAt",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#status"] = "status"
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":status"] = new(body.Status),
                        [":adminComment"] = body.AdminComment is null
                            ? new AttributeValue { NULL = true }
                            : new AttributeValue(body.AdminComment),
                        [":reviewedBy"] = new(user.Email),
                        [":updatedAt"] = new(DateTime.UtcNow.ToString("O"))
                    },
                    ReturnValues = ReturnValue.ALL_NEW
                });

                return Json(await WithReceiptUrl(FromItem(update.Attributes)));
            }

            return Json(new ErrorResponse("Route introuvable"), HttpStatusCode.NotFound);
        }
        catch (ApiException error)
        {
            return Json(new ErrorResponse(error.Message), error.StatusCode);
        }
        catch (Exception error)
        {
            context.Logger.LogError(error.ToString());
            return Json(new ErrorResponse("Erreur Lambda"), HttpStatusCode.InternalServerError);
        }
    }

    private AppUser GetUser(APIGatewayHttpApiV2ProxyRequest request)
    {
        var claims = request.RequestContext.Authorizer?.Jwt?.Claims;

        if (claims is null || !claims.TryGetValue("sub", out var sub))
        {
            throw new ApiException("Authentification requise", HttpStatusCode.Unauthorized);
        }

        claims.TryGetValue("email", out var email);
        claims.TryGetValue("name", out var name);
        claims.TryGetValue("given_name", out var givenName);
        claims.TryGetValue("cognito:groups", out var rawGroups);

        var groups = ParseGroups(rawGroups);

        return new AppUser(
            sub,
            email ?? "unknown@example.com",
            name ?? givenName ?? email ?? "Utilisateur",
            groups
        );
    }

    private static void AssertAdmin(AppUser user)
    {
        if (!user.Groups.Contains("ADMIN"))
        {
            throw new ApiException("Acces refuse", HttpStatusCode.Forbidden);
        }
    }

    private static HashSet<string> ParseGroups(string? rawGroups)
    {
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawGroups))
        {
            return groups;
        }

        var value = rawGroups.Trim();
        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                var jsonGroups = JsonSerializer.Deserialize<string[]>(value, JsonOptions);
                if (jsonGroups is not null)
                {
                    foreach (var group in jsonGroups.Where(group => !string.IsNullOrWhiteSpace(group)))
                    {
                        groups.Add(group.Trim());
                    }

                    return groups;
                }
            }
            catch (JsonException)
            {
                // Fall through to the tolerant comma parser below.
            }
        }

        foreach (var group in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalizedGroup = group.Trim().Trim('[', ']', '"', '\'');
            if (!string.IsNullOrWhiteSpace(normalizedGroup))
            {
                groups.Add(normalizedGroup);
            }
        }

        return groups;
    }

    private static T ParseBody<T>(APIGatewayHttpApiV2ProxyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Body))
        {
            throw new ApiException("Corps JSON manquant", HttpStatusCode.BadRequest);
        }

        var json = request.IsBase64Encoded
            ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(request.Body))
            : request.Body;

        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new ApiException("Corps JSON invalide", HttpStatusCode.BadRequest);
    }

    private async Task<IReadOnlyList<ExpenseRequest>> WithReceiptUrls(IEnumerable<ExpenseRequest> expenses)
    {
        var results = new List<ExpenseRequest>();

        foreach (var expense in expenses)
        {
            results.Add(await WithReceiptUrl(expense));
        }

        return results;
    }

    private Task<ExpenseRequest> WithReceiptUrl(ExpenseRequest expense)
    {
        if (string.IsNullOrWhiteSpace(expense.ReceiptS3Key))
        {
            return Task.FromResult(expense);
        }

        var url = s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = expense.ReceiptS3Key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(5)
        });

        return Task.FromResult(expense with { ReceiptUrl = url });
    }

    private APIGatewayHttpApiV2ProxyResponse Json(object body, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = (int)statusCode,
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["Access-Control-Allow-Origin"] = appOrigin,
                ["Access-Control-Allow-Headers"] = "content-type,authorization",
                ["Access-Control-Allow-Methods"] = "GET,POST,PATCH,OPTIONS"
            },
            Body = JsonSerializer.Serialize(body, JsonOptions)
        };
    }

    private static Dictionary<string, AttributeValue> ToItem(ExpenseRequest expense)
    {
        return new Dictionary<string, AttributeValue>
        {
            ["requestId"] = new(expense.RequestId),
            ["employeeId"] = new(expense.EmployeeId),
            ["employeeEmail"] = new(expense.EmployeeEmail),
            ["employeeName"] = new(expense.EmployeeName),
            ["amount"] = new() { N = expense.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            ["currency"] = new(expense.Currency),
            ["category"] = new(expense.Category),
            ["description"] = new(expense.Description),
            ["receiptS3Key"] = new(expense.ReceiptS3Key),
            ["receiptFileName"] = new(expense.ReceiptFileName),
            ["receiptContentType"] = new(expense.ReceiptContentType),
            ["status"] = new(expense.Status),
            ["createdAt"] = new(expense.CreatedAt),
            ["updatedAt"] = new(expense.UpdatedAt)
        };
    }

    private static ExpenseRequest FromItem(Dictionary<string, AttributeValue> item)
    {
        return new ExpenseRequest(
            GetString(item, "requestId"),
            GetString(item, "employeeId"),
            GetString(item, "employeeEmail"),
            GetString(item, "employeeName"),
            decimal.Parse(GetNumber(item, "amount"), System.Globalization.CultureInfo.InvariantCulture),
            GetString(item, "currency"),
            GetString(item, "category"),
            GetString(item, "description"),
            GetString(item, "receiptS3Key"),
            GetString(item, "receiptFileName"),
            GetString(item, "receiptContentType"),
            null,
            null,
            GetString(item, "status"),
            GetOptionalString(item, "adminComment"),
            GetOptionalString(item, "reviewedBy"),
            GetString(item, "createdAt"),
            GetString(item, "updatedAt")
        );
    }

    private static string GetString(Dictionary<string, AttributeValue> item, string key) => item[key].S;

    private static string GetNumber(Dictionary<string, AttributeValue> item, string key) => item[key].N;

    private static string? GetOptionalString(Dictionary<string, AttributeValue> item, string key)
    {
        return item.TryGetValue(key, out var value) && value.NULL != true
            ? value.S
            : null;
    }

    private static string SafeFileName(string fileName)
    {
        var validChars = fileName.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character
                : '-');

        return new string(validChars.ToArray())[..Math.Min(fileName.Length, 120)];
    }
}

public sealed record AppUser(string Sub, string Email, string Name, HashSet<string> Groups);

public sealed record UploadUrlRequest(string FileName, string ContentType);

public sealed record UploadUrlResponse(
    string Mode,
    string Key,
    string UploadUrl,
    string Method,
    Dictionary<string, string> Headers);

public sealed record CreateExpenseRequest(
    decimal Amount,
    string Category,
    string Description,
    string ReceiptS3Key,
    string ReceiptFileName,
    string ReceiptContentType);

public sealed record DecisionRequest(string Status, string? AdminComment);

public sealed record ExpenseRequest(
    string RequestId,
    string EmployeeId,
    string EmployeeEmail,
    string EmployeeName,
    decimal Amount,
    string Currency,
    string Category,
    string Description,
    string ReceiptS3Key,
    string ReceiptFileName,
    string ReceiptContentType,
    string? ReceiptPreviewDataUrl,
    string? ReceiptUrl,
    string Status,
    string? AdminComment,
    string? ReviewedBy,
    string CreatedAt,
    string UpdatedAt);

public sealed record ErrorResponse(string Error);

public sealed class ApiException(string message, HttpStatusCode statusCode) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
