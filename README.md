# NotesDeFrais

Application .NET MAUI de gestion de notes de frais, connectee a une stack AWS serverless. Le projet couvre deux profils :

- Employe : connexion, creation d'une demande de remboursement, ajout d'un justificatif image, suivi de ses demandes.
- Admin : connexion, consultation des demandes par statut, ouverture du justificatif, acceptation ou refus avec commentaire.

## Etat actuel

Le client MAUI est fonctionnel avec :

- Authentification Cognito Hosted UI en OAuth code flow avec PKCE.
- Stockage local des tokens, avec fallback pour les builds MacCatalyst locaux.
- Configuration saisissable dans l'app, sans secrets en dur.
- Upload de justificatif vers S3 via URL presignee.
- Creation et listing des demandes via API Gateway + Lambda.
- Espace admin visible uniquement quand le token Cognito contient le groupe `ADMIN`.
- Sur MacCatalyst, un champ de chemin local permet de charger un justificatif si le picker natif MAUI se comporte mal.

Le backend AWS se trouve dans [legacy/aws](legacy/aws) et utilise SAM.

## Architecture rapide

```mermaid
flowchart LR
  User["Employe / Admin"] --> App["Application MAUI"]
  App --> Cognito["Amazon Cognito Hosted UI"]
  App --> Api["API Gateway HTTP"]
  Api --> Lambda["Lambda .NET 8"]
  Lambda --> DynamoDB["DynamoDB demandes"]
  Lambda --> S3["S3 justificatifs"]
  Lambda --> Api
```

Voir [stack.md](stack.md) pour le schema detaille et les flux.

## Prerequis

- .NET SDK avec workload MAUI.
- AWS CLI configure.
- AWS SAM CLI.
- Un compte AWS autorise a creer Cognito, API Gateway, Lambda, DynamoDB, S3 et les roles IAM SAM.

Verification locale utile :

```bash
dotnet --info
sam --version
aws sts get-caller-identity
```

## Deployer le backend

Depuis le dossier AWS :

```bash
cd legacy/aws

sam deploy \
  --parameter-overrides \
  AppOrigin="http://localhost:3000" \
  NativeCallbackUrl="notesdefrais://auth" \
  NativeLogoutUrl="notesdefrais://signout" \
  EnableCognitoHostedUiDomain="true" \
  CognitoDomainPrefix="notesdefrais-votre-prefixe"
```

Apres deploiement, recuperer les sorties :

```bash
aws cloudformation describe-stacks \
  --region eu-west-3 \
  --stack-name expense-api \
  --query "Stacks[0].Outputs"
```

Les valeurs importantes sont :

- `ExpenseApiUrl`
- `CognitoHostedUiDomain`
- `CognitoClientId`
- `CognitoUserPoolId`

## Configurer l'application

Le plus simple est d'ouvrir l'app et de remplir les champs de configuration :

- API Gateway : valeur `ExpenseApiUrl`.
- Domaine Cognito : valeur `CognitoHostedUiDomain`.
- Client id Cognito : valeur `CognitoClientId`.
- URI de retour : `notesdefrais://auth`.

Cliquer ensuite sur `Enregistrer`, puis `Connexion Cognito`.

L'application lit aussi ces variables d'environnement si elles existent :

- `EXPENSE_API_URL`
- `COGNITO_DOMAIN`
- `COGNITO_CLIENT_ID`
- `COGNITO_REDIRECT_URI`
- `COGNITO_LOGOUT_URI`

Le fichier `appSettings.json` present dans le depot sert de reference locale, mais le code actuel charge la configuration depuis les variables d'environnement ou depuis les preferences enregistrees par l'ecran de configuration.

## Creer les comptes

Les utilisateurs sont geres dans Cognito. Les roles viennent des groupes Cognito :

- `EMPLOYEE`
- `ADMIN`

Ajouter un utilisateur au groupe employe :

```bash
aws cognito-idp admin-add-user-to-group \
  --region eu-west-3 \
  --user-pool-id "USER_POOL_ID" \
  --username "user@example.com" \
  --group-name EMPLOYEE
```

Ajouter un utilisateur au groupe admin :

```bash
aws cognito-idp admin-add-user-to-group \
  --region eu-west-3 \
  --user-pool-id "USER_POOL_ID" \
  --username "admin@example.com" \
  --group-name ADMIN
```

Apres un changement de groupe, il faut se deconnecter puis se reconnecter dans l'app pour obtenir un nouveau token Cognito.

## Utilisation

Employe :

1. Se connecter avec Cognito.
2. Renseigner montant, categorie et description.
3. Choisir un justificatif image.
4. Sur MacCatalyst, si le picker ne charge pas le fichier, coller le chemin complet dans le champ de chemin local puis cliquer `Charger`.
5. Envoyer la demande.

Admin :

1. Se connecter avec un compte dans le groupe `ADMIN`.
2. Ouvrir la section `Validation admin`.
3. Filtrer par statut `PENDING`, `APPROVED` ou `REJECTED`.
4. Ouvrir le justificatif avec le bouton `Justificatif`.
5. Accepter ou refuser la demande.

## Commandes de verification

Compiler le client MAUI cote code partage :

```bash
dotnet build NotesDeFrais.csproj -f net10.0-android -t:CoreCompile
```

Compiler la Lambda :

```bash
dotnet build legacy/aws/lambdas/expense-api/ExpenseApi.csproj
```

Valider le template SAM :

```bash
sam validate --lint --template-file legacy/aws/template.yaml
```

## Depannage

`Acces refuse` cote admin :

- Verifier que l'utilisateur est dans le groupe `ADMIN`.
- Se deconnecter puis se reconnecter pour regenerer le token.
- Verifier que l'app pointe vers le bon User Pool et le bon client Cognito.

`Justificatif requis` alors qu'un fichier a ete choisi :

- Sur MacCatalyst, utiliser le champ de chemin local et cliquer `Charger`.
- Verifier que le fichier est en `.jpg`, `.jpeg`, `.png`, `.webp` ou `.gif`.

Erreur de domaine Cognito lors du changement de prefixe :

- Voir la section dediee dans [legacy/aws/README.md](legacy/aws/README.md).

## Structure

```text
.
|-- MainPage.xaml / MainPage.xaml.cs     Interface MAUI principale
|-- Models/                              Modeles de demandes et utilisateur
|-- Services/                            Auth Cognito, client API, config, JWT
|-- Platforms/                           Configuration native Android/iOS/Mac
|-- legacy/aws/template.yaml             Stack SAM
|-- legacy/aws/lambdas/expense-api/      Lambda .NET 8
|-- legacy/aws/README.md                 Documentation AWS detaillee
|-- stack.md                             Architecture et flux
```
