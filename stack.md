# Architecture

Ce document decrit le fonctionnement technique de l'application NotesDeFrais.

## Vue d'ensemble

```mermaid
flowchart LR
  Employee["Employe"] --> Maui["Application .NET MAUI"]
  Admin["Admin"] --> Maui
  Maui --> Cognito["Amazon Cognito Hosted UI"]
  Maui --> ApiGateway["API Gateway HTTP"]
  ApiGateway --> Authorizer["JWT Authorizer Cognito"]
  Authorizer --> Lambda["Lambda expense-api .NET 8"]
  Lambda --> DynamoDB["DynamoDB aws-expense-desk-requests"]
  Lambda --> S3["S3 justificatifs"]
  Lambda --> ApiGateway
```

## Flux d'authentification

```mermaid
sequenceDiagram
  participant U as Utilisateur
  participant A as App MAUI
  participant C as Cognito Hosted UI
  participant API as API Gateway
  participant L as Lambda

  U->>A: Connexion Cognito
  A->>C: OAuth code flow + PKCE
  C-->>A: Callback notesdefrais://auth
  A->>C: Echange du code contre tokens
  A->>A: Lecture du JWT et des groupes
  A->>API: Requete avec Authorization: Bearer id_token
  API->>API: Validation JWT
  API->>L: Claims Cognito
```

Le role applicatif depend du claim `cognito:groups` :

- `EMPLOYEE` : peut creer et consulter ses demandes.
- `ADMIN` : peut consulter les demandes par statut et les accepter/refuser.

La Lambda accepte plusieurs formats de groupes (`ADMIN`, `ADMIN,EMPLOYEE`, ou `["ADMIN"]`) pour rester compatible avec le format transmis par API Gateway.

## Flux de creation d'une demande

```mermaid
sequenceDiagram
  participant A as App MAUI
  participant API as API Gateway
  participant L as Lambda
  participant S3 as S3
  participant DDB as DynamoDB

  A->>API: POST /expenses/upload-url
  API->>L: Demande d'URL presignee
  L-->>A: URL PUT S3 + cle objet
  A->>S3: PUT image justificatif
  A->>API: POST /expenses avec montant, categorie, description, cle S3
  API->>L: Creation demande
  L->>DDB: PutItem status=PENDING
  L-->>A: Demande creee + URL GET presignee
```

## Flux admin

```mermaid
sequenceDiagram
  participant A as App Admin
  participant API as API Gateway
  participant L as Lambda
  participant DDB as DynamoDB
  participant S3 as S3

  A->>API: GET /expenses?admin=true&status=PENDING
  API->>L: Claims Cognito avec groupe ADMIN
  L->>DDB: Query status-createdAt-index
  L->>S3: Generation URL GET presignee par justificatif
  L-->>A: Liste des demandes
  A->>API: PATCH /admin/expenses/{requestId}
  L->>DDB: Update status, adminComment, reviewedBy
  L-->>A: Demande mise a jour
```

## Ressources AWS

La stack SAM cree :

- `ExpenseUserPool` : User Pool Cognito.
- `ExpenseUserPoolClient` : client OAuth public, sans secret.
- `ExpenseUserPoolDomain` : domaine Hosted UI optionnel.
- `EmployeeGroup` et `AdminGroup` : groupes de role.
- `ExpenseHttpApi` : API Gateway HTTP protegee par JWT authorizer Cognito.
- `ExpenseApiFunction` : Lambda .NET 8.
- `ExpenseTable` : table DynamoDB avec index par employe et par statut.
- `ReceiptsBucket` : bucket S3 prive pour les justificatifs.

## Endpoints API

Toutes les routes sont protegees par Cognito.

| Methode | Route | Role | Description |
| --- | --- | --- | --- |
| `POST` | `/expenses/upload-url` | Connecte | Genere une URL S3 presignee pour upload |
| `POST` | `/expenses` | Connecte | Cree une demande de remboursement |
| `GET` | `/expenses` | Connecte | Liste les demandes de l'utilisateur courant |
| `GET` | `/expenses?admin=true&status=PENDING` | `ADMIN` | Liste les demandes par statut |
| `PATCH` | `/admin/expenses/{requestId}` | `ADMIN` | Accepte ou refuse une demande |

## Donnees DynamoDB

Cle primaire :

- `requestId`

Index :

- `employeeId-createdAt-index` pour le suivi employe.
- `status-createdAt-index` pour le listing admin.

Champs principaux :

- `requestId`
- `employeeId`
- `employeeEmail`
- `employeeName`
- `amount`
- `currency`
- `category`
- `description`
- `receiptS3Key`
- `receiptFileName`
- `receiptContentType`
- `status`
- `adminComment`
- `reviewedBy`
- `createdAt`
- `updatedAt`

## Particularites MacCatalyst

Le picker de fichier MAUI peut etre instable selon la version macOS / MAUI. L'application propose donc deux chemins :

- bouton `Choisir un justificatif` ;
- champ de chemin local + bouton `Charger`.

Le submit utilise les bytes charges en memoire, pas directement le `FileResult`, afin de fiabiliser l'upload S3.
