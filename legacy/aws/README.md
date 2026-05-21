# Backend AWS

Ce dossier contient la stack SAM du backend NotesDeFrais.

## Contenu

```text
legacy/aws
|-- template.yaml                  Definition SAM
|-- samconfig.toml                 Parametres de deploiement par defaut
|-- lambdas/expense-api/           Lambda .NET 8
|   |-- Function.cs
|   |-- ExpenseApi.csproj
```

## Ressources creees

- Cognito User Pool avec login par email.
- Client Cognito OAuth public sans secret.
- Domaine Cognito Hosted UI.
- Groupes Cognito `EMPLOYEE` et `ADMIN`.
- API Gateway HTTP avec authorizer JWT Cognito.
- Lambda .NET 8 `expense-api`.
- Table DynamoDB `aws-expense-desk-requests`.
- Bucket S3 prive pour les justificatifs.

## Parametres SAM

| Parametre | Defaut | Description |
| --- | --- | --- |
| `AppOrigin` | `http://localhost:3000` | Origine autorisee pour CORS API/S3 |
| `NativeCallbackUrl` | `notesdefrais://auth` | Callback Cognito de l'app MAUI |
| `NativeLogoutUrl` | `notesdefrais://signout` | URL de logout de l'app MAUI |
| `CognitoDomainPrefix` | vide | Prefixe Hosted UI. Si vide, utilise `stack-name-account-id` |
| `EnableCognitoHostedUiDomain` | `true` | Permet de desactiver temporairement le domaine lors d'un remplacement |

## Deploiement

Depuis ce dossier :

```bash
sam validate --lint --template-file template.yaml

sam deploy \
  --parameter-overrides \
  AppOrigin="http://localhost:3000" \
  NativeCallbackUrl="notesdefrais://auth" \
  NativeLogoutUrl="notesdefrais://signout" \
  EnableCognitoHostedUiDomain="true" \
  CognitoDomainPrefix="notesdefrais-votre-prefixe"
```

Si `CognitoDomainPrefix` est omis ou vide, le template utilise un prefixe base sur le nom de stack et l'ID de compte AWS.

## Outputs utiles

```bash
aws cloudformation describe-stacks \
  --region eu-west-3 \
  --stack-name expense-api \
  --query "Stacks[0].Outputs"
```

Outputs a reporter dans l'app MAUI :

- `ExpenseApiUrl` vers le champ `API Gateway`.
- `CognitoHostedUiDomain` vers le champ `Domaine Cognito`.
- `CognitoClientId` vers le champ `Client id Cognito`.
- `CognitoUserPoolId` pour les commandes d'administration Cognito.

## Gestion des utilisateurs

Lister les groupes d'un utilisateur :

```bash
aws cognito-idp admin-list-groups-for-user \
  --region eu-west-3 \
  --user-pool-id "USER_POOL_ID" \
  --username "user@example.com"
```

Ajouter un employe :

```bash
aws cognito-idp admin-add-user-to-group \
  --region eu-west-3 \
  --user-pool-id "USER_POOL_ID" \
  --username "user@example.com" \
  --group-name EMPLOYEE
```

Ajouter un admin :

```bash
aws cognito-idp admin-add-user-to-group \
  --region eu-west-3 \
  --user-pool-id "USER_POOL_ID" \
  --username "admin@example.com" \
  --group-name ADMIN
```

Si l'utilisateur se connecte par email mais que la commande ne cible pas le bon identifiant, recuperer le `Username` exact :

```bash
aws cognito-idp list-users \
  --region eu-west-3 \
  --user-pool-id "USER_POOL_ID" \
  --filter 'email = "admin@example.com"'
```

Apres ajout a un groupe, l'utilisateur doit se deconnecter puis se reconnecter dans l'app pour obtenir un nouveau token.

## Routes API

Toutes les routes demandent un JWT Cognito valide.

### `POST /expenses/upload-url`

Genere une URL S3 presignee pour envoyer un justificatif.

Corps :

```json
{
  "fileName": "ticket.png",
  "contentType": "image/png"
}
```

### `POST /expenses`

Cree une demande en statut `PENDING`.

Corps :

```json
{
  "amount": 42.5,
  "category": "Repas",
  "description": "Dejeuner client",
  "receiptS3Key": "receipts/user/id-ticket.png",
  "receiptFileName": "ticket.png",
  "receiptContentType": "image/png"
}
```

### `GET /expenses`

Liste les demandes de l'utilisateur connecte.

### `GET /expenses?admin=true&status=PENDING`

Liste les demandes du statut demande. Role `ADMIN` requis.

### `PATCH /admin/expenses/{requestId}`

Accepte ou refuse une demande. Role `ADMIN` requis.

Corps :

```json
{
  "status": "APPROVED",
  "adminComment": "OK"
}
```

`status` doit valoir `APPROVED` ou `REJECTED`.

## Justificatifs

Les justificatifs sont stockes dans S3 avec des cles de type :

```text
receipts/{sub}/{guid}-{fileName}
```

La Lambda ne rend pas le bucket public. Elle renvoie des URL presignees courtes :

- PUT pour upload depuis l'app ;
- GET pour consultation employe/admin.

## Changer le prefixe Cognito

CloudFormation ne sait pas toujours remplacer directement `AWS::Cognito::UserPoolDomain`, car Cognito n'accepte qu'un domaine Hosted UI actif par User Pool. Faire deux deploiements.

Desactiver temporairement le domaine :

```bash
sam deploy \
  --parameter-overrides \
  AppOrigin="http://localhost:3000" \
  NativeCallbackUrl="notesdefrais://auth" \
  NativeLogoutUrl="notesdefrais://signout" \
  EnableCognitoHostedUiDomain="false"
```

Puis recreer le domaine avec le nouveau prefixe :

```bash
sam deploy \
  --parameter-overrides \
  AppOrigin="http://localhost:3000" \
  NativeCallbackUrl="notesdefrais://auth" \
  NativeLogoutUrl="notesdefrais://signout" \
  EnableCognitoHostedUiDomain="true" \
  CognitoDomainPrefix="notesdefrais-votre-prefixe"
```

## Supprimer la stack

```bash
aws cloudformation delete-stack \
  --region eu-west-3 \
  --stack-name expense-api

aws cloudformation wait stack-delete-complete \
  --region eu-west-3 \
  --stack-name expense-api
```

Si la suppression echoue car le bucket S3 contient des justificatifs, vider le bucket puis relancer la suppression.

## Verification locale

Compiler la Lambda :

```bash
dotnet build lambdas/expense-api/ExpenseApi.csproj
```

Valider le template :

```bash
sam validate --lint --template-file template.yaml
```
