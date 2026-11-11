// Production.
//
// Secrets are read from the environment rather than written here, so this file
// is safe in the repository and there is one fewer place a password can be
// committed by accident.
using 'main.bicep'

param environmentName = 'prod'
// East US 2, not Central US like staging.
//
// Both portals serve the API from their own origin by rewriting to harbor --
// deliberately, because a SameSite=Lax session cookie is not sent on a
// cross-site fetch. So every API call is relayed by Vercel, and Vercel serves
// this project from iad1, in northern Virginia. Central US is Iowa, which made
// the relay a cross-country round trip on every single request.
//
// East US 2 is the same metropolitan area as iad1, so that hop stops being a
// hop worth measuring. It is also where the people using this are: Maryland,
// and the schools around it.
//
// Postgres is cheaper here too -- B1ms is $0.017/hour against $0.01921 in
// Central US -- and Container Apps is priced identically. Staging stays in
// Central US for now; it is moving subscriptions separately and should be
// created here when it does.
param location = 'eastus2'


param imageTag = readEnvironmentVariable('IMAGE_TAG')

// The images live on ghcr.io now. GHCR_TOKEN is a GitHub token with
// read:packages and nothing else, and it is the only credential here that
// has to be rotated by hand -- the registry it replaced was pulled from with
// a managed identity. See apps.bicep.
param ghcrRepository = readEnvironmentVariable('GHCR_REPOSITORY', 'morgan-hacks/arctic')
param ghcrUsername = readEnvironmentVariable('GHCR_USERNAME')
param ghcrToken = readEnvironmentVariable('GHCR_TOKEN')
param dbPassword = readEnvironmentVariable('DB_PASSWORD')

// The database the services talk to, when it is not the Azure server this
// deployment creates. All three default to empty, which means that server --
// so an environment nobody has moved is unaffected, and moving one back is
// clearing a variable rather than reverting a deploy.
//
// ARCTIC_DB is the pooled endpoint the services use. ARCTIC_DB_DIRECT is the
// same database unpooled, for migrations and dumps. ARCTIC_DB_KEDA is the same
// database again in libpq syntax, because the scaler that wakes lark cannot
// read the other two. See apps.bicep for why that is three values and not one.
param appConnectionString = readEnvironmentVariable('ARCTIC_DB', '')
param appConnectionStringDirect = readEnvironmentVariable('ARCTIC_DB_DIRECT', '')
param appKedaConnection = readEnvironmentVariable('ARCTIC_DB_KEDA', '')
param superAdminEmail = readEnvironmentVariable('SUPER_ADMIN_EMAIL')
param sentryDsn = readEnvironmentVariable('SENTRY_DSN', '')

param awsRegion = readEnvironmentVariable('AWS_REGION', '')
param awsAccessKeyId = readEnvironmentVariable('AWS_ACCESS_KEY_ID', '')
param awsSecretAccessKey = readEnvironmentVariable('AWS_SECRET_ACCESS_KEY', '')

// Empty means SES reports nothing back about a send. See apps.bicep.
param sesConfigurationSet = readEnvironmentVariable('SES_CONFIGURATION_SET', '')

param googleClientId = readEnvironmentVariable('GOOGLE_CLIENT_ID', '')
param googleClientSecret = readEnvironmentVariable('GOOGLE_CLIENT_SECRET', '')
param googleRedirectUri = readEnvironmentVariable('GOOGLE_REDIRECT_URI', '')

// The portalweb origin. Emailed sign-in links are built from it.
param publicBaseUrl = readEnvironmentVariable('PUBLIC_BASE_URL', '')
param consoleBaseUrl = readEnvironmentVariable('CONSOLE_BASE_URL', '')

// The portalforms origin. A sign-in link for a form lands here instead, so the
// session cookie is set on the host the form is actually served from.
param formsBaseUrl = readEnvironmentVariable('FORMS_BASE_URL', '')

// Keep one web-facing replica warm. Set WARM_REPLICAS=0 explicitly to allow
// idle services to sleep and accept cold starts.
// Unset and empty have to mean the same thing here: a GitHub variable that
// does not exist arrives as an empty string, and int('') fails the whole
// deployment rather than the parameter.
param warmReplicas = int(empty(readEnvironmentVariable('WARM_REPLICAS', '1'))
  ? '1'
  : readEnvironmentVariable('WARM_REPLICAS', '1'))

// Keep the mail worker running. Zero lets it sleep until a message is actually
// queued, which only works because apps.bicep adds a scale rule when this is
// zero. Same empty-string handling as warmReplicas above, and for the same
// reason: int('') fails the deployment rather than the parameter.
param larkWarmReplicas = int(empty(readEnvironmentVariable('LARK_WARM_REPLICAS', '1'))
  ? '1'
  : readEnvironmentVariable('LARK_WARM_REPLICAS', '1'))

// CPU and memory per replica, one of the sizes apps.bicep allows. The fallback
// is the size these services have always run, not the cheapest one: less CPU
// also means a slower cold start. Drop it to 0.25 on an environment nobody
// waits on, raise it for an open application window. Same empty-string
// handling as the two above: an unset GitHub variable arrives as '', which
// would otherwise be rejected by @allowed and fail the whole deployment.
param containerSize = empty(readEnvironmentVariable('CONTAINER_SIZE', '0.5'))
  ? '0.5'
  : readEnvironmentVariable('CONTAINER_SIZE', '0.5')

// Shared secret proving a request reached harbor through one of our front
// ends. Empty means forwarded addresses are never believed, which is a coarser
// rate limit rather than an absent one -- so a missing variable degrades
// safely.
// Whether the applicant portal is served. Empty leaves features.json to decide,
// which is how a flag stays off everywhere until somebody turns it on for one
// environment. Set ENABLE_HACKER_PORTAL_FEATURE on the GitHub environment.
param enableHackerPortalFeature = readEnvironmentVariable('ENABLE_HACKER_PORTAL_FEATURE', '')

param proxySecret = readEnvironmentVariable('PROXY_SHARED_SECRET', '')

// Empty leaves the word table in charge rather than breaking the assistant.
param openAiApiKey = readEnvironmentVariable('OPENAI_API_KEY', '')

param deployPlatform = bool(readEnvironmentVariable('DEPLOY_PLATFORM', 'true'))
param deployApps = bool(readEnvironmentVariable('DEPLOY_APPS', 'true'))

param enableHackerDirectoryFeature = readEnvironmentVariable('ENABLE_DIRECTORY_FEATURE', '')
