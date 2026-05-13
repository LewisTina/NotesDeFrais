# Stack AWS Notes de frais

Cette stack expose le backend utilise par l'application MAUI :

- Amazon Cognito pour la connexion employe/admin.
- API Gateway HTTP + Lambda .NET 8 pour les demandes.
- DynamoDB pour les notes de frais.
- S3 avec URLs presignees pour les justificatifs.

## Deploiement

Par defaut, le domaine Cognito Hosted UI utilise `stack-name-account-id`, ce qui evite d'avoir a choisir un nom globalement unique. Tu peux quand meme forcer un prefixe :

```bash
sam deploy \
  --parameter-overrides \
  AppOrigin="http://localhost:3000" \
  NativeCallbackUrl="notesdefrais://auth" \
  NativeLogoutUrl="notesdefrais://signout" \
  CognitoDomainPrefix="notesdefrais-viggo-lewis"
```

Apres le deploiement, renseigner dans l'application MAUI :

- `ExpenseApiUrl` dans le champ API Gateway.
- `CognitoHostedUiDomain` dans le champ Domaine Cognito.
- `CognitoClientId` dans le champ Client id Cognito.
- `notesdefrais://auth` dans le champ URI de retour.

Les memes valeurs peuvent aussi etre fournies via variables d'environnement lors du lancement local :

- `EXPENSE_API_URL`
- `COGNITO_DOMAIN`
- `COGNITO_CLIENT_ID`
- `COGNITO_REDIRECT_URI`
- `COGNITO_LOGOUT_URI`

Les utilisateurs doivent etre ajoutes au groupe Cognito `EMPLOYEE` ou `ADMIN`. L'application affiche l'espace de validation uniquement si le jeton Cognito contient le groupe `ADMIN`.

## Changer le prefixe Cognito

CloudFormation ne sait pas toujours remplacer directement `AWS::Cognito::UserPoolDomain`, car Cognito n'accepte qu'un domaine Hosted UI actif par User Pool. Faire deux deploiements :

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
  CognitoDomainPrefix="notesdefrais-votre-nom"
```
