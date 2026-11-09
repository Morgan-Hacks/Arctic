// The three services.
//
// Deployed after platform.bicep and after the migration job has succeeded, so
// nothing here ever runs against a schema it does not expect.

targetScope = 'resourceGroup'

@allowed(['staging', 'prod'])
param environmentName string

param location string = resourceGroup().location

@description('Commit sha. Never "latest" — a rollback has to be a tag that already exists.')
param imageTag string

param registryName string
param registryResourceGroup string = 'rg-mh-shared'

@secure()
param dbPassword string

param dbAdminUser string = 'arctic'
param dbName string = 'morganhacks'

@description('''
A whole connection string for the database the services should use, when that
is not the Azure server platform.bicep provisions. Empty means that server, so
an environment nobody has moved is untouched.

This is the pooled endpoint where the database has one. Three long-lived
services opening connections as they wake is what a pooler is for, and the
things a pooler is wrong for -- migrations, dumps -- do not read this value.
''')
@secure()
param appConnectionString string = ''

@description('''
The same database in libpq syntax, for the KEDA scaler below.

A third rendering of one credential, for the reason the comment above
kedaConnectionString gives at length: KEDA hands this to libpq, libpq does not
read Npgsql keyword syntax, and the failure is silent. Space-separated
keywords, or the postgresql:// URI libpq also accepts.

Empty means the Azure server. Required whenever appConnectionString is set and
larkWarmReplicas is zero, because that is the combination where lark has
nothing but this to wake it.
''')
@secure()
param appKedaConnection string = ''

@secure()
@description('Empty disables error reporting, which is what lets this run with no accounts.')
param sentryDsn string = ''

@description('''
Region for SES. Empty means lark runs but sends nothing and says so, rather
than crash-looping on a missing variable — which is what it used to do.
''')
param awsRegion string = ''

// Which SES configuration set each send is tagged with. Without one SES
// publishes no bounce, complaint or delivery events, so notify.suppressions
// never learns about a dead address and every message stops at 'sent'.
// Empty is allowed and degrades to exactly the old behaviour.
param sesConfigurationSet string = ''

@secure()
param awsAccessKeyId string = ''

@secure()
param awsSecretAccessKey string = ''

@description('Google OAuth client id. Empty leaves organizer sign-in answering 503.')
param googleClientId string = ''

@secure()
param googleClientSecret string = ''

@description('''
Must match, character for character, the URI registered with Google — and it is
the origin the *browser* lands on, which is the admin app rather than harbor,
because the admin app proxies the API from its own origin.
''')
param googleRedirectUri string = ''

@description('''
The origin applicants reach the portal on — portalweb, not harbor.

Emailed sign-in links are built from this, and the link both sets the session
cookie and lands the person on /portal, so it has to be the host they will
actually be browsing: a cookie set on the API's own origin is one the portal is
never sent. Empty falls back to http://localhost:3000 in atlas, which in a
deployed environment means every link points at a machine nobody is running.
''')
param publicBaseUrl string = ''

@description('''
Where the organizer console is: the portaladmin origin. A new organizer's
welcome email links to it, and an empty value means that email links nowhere.
Not the same as googleRedirectUri, which is this origin plus a callback path.
''')
param consoleBaseUrl string = ''

@description('''
The origin the public forms site is on — portalforms, not portalweb and not
harbor.

A form that requires sign-in emails a link, and that link both sets the session
cookie and lands the person back on the form. The cookie is host-only, so the
link has to be on this origin or the person arrives at a form they are still
signed out of, holding a perfectly good session for a different hostname. Empty
falls back to http://localhost:3002 in atlas, which is the port
deploy/local/dev.sh uses.
''')
param formsBaseUrl string = ''

@description('Resource id of the identity that pulls images.')
param pullIdentityId string

@description('''
Client id of that same identity, which is what the Azure SDK inside atlas needs
to authenticate as it. A container app with a user-assigned identity attached
still defaults to the system-assigned one, which holds no roles — so leaving
this empty makes every resume upload fail as if the role assignment were
missing.
''')
param pullIdentityClientId string

@description('''
How many replicas of the web-facing services to keep warm.

One keeps the API and gateway ready between requests. Zero permits both
services to sleep and makes the first request wait for them to start.
Choose zero explicitly when accepting that delay to reduce idle hosting usage.

This does not apply to lark, which polls a queue on a timer and therefore has
nothing to wake it. It stays at one replica and is most of the idle cost.
''')
@minValue(0)
@maxValue(3)
param warmReplicas int = 1

@description('''
How many replicas of the mail worker to keep running.

One is the safe answer and the default. Zero is only viable together with the
scale rule below, because nothing else would ever wake a worker that polls on a
timer -- and a queue with no worker stops sending while every dashboard reads
green.

Set LARK_WARM_REPLICAS=0 on an environment where a few seconds of delay before
the first message of a batch is cheaper than a replica sitting idle all month.
Production is not that environment.
''')
@minValue(0)
@maxValue(1)
param larkWarmReplicas int = 1

@description('''
How much CPU and memory each replica gets, as one choice rather than two.

A size and not a pair of numbers on purpose. Container Apps only accepts CPU
and memory in a fixed 1:2 ratio, and it does not tell you that until the
deployment is already running -- so two independent parameters would let a
plausible-looking combination fail halfway through a release. One allowed value
indexing a table of known-good pairs cannot be set wrong, and a typo is
rejected while the template is still being read.

0.5 is the default, which is what these services have always run, and the
default is deliberately not the cheapest option. Measured working set over a
quiet week was 159-171 MiB for atlas and harbor and 272 MiB for lark, so 0.25
fits all three with room -- but CPU is also what starts a .NET process, and
halving it lengthens a cold start that the README already measures at about 22
seconds. Somewhere around 40 is not a saving, it is a worse product.

It is also worth very little. Against a replica floor of zero, scaling to
sleep is the whole saving and the size is a rounding error: at the awake time
these services actually accumulate, 0.25 costs nothing per month and 0.5 costs
about 65 cents. Trading twenty seconds of everybody's first request for eight
dollars a year is not a trade worth making where anyone is watching.

So: set CONTAINER_SIZE=0.25 on an environment nobody waits on, the same way
WARM_REPLICAS=0 is opted into rather than assumed. And raise it the other way
-- 0.75, 1.0 -- for the weeks around an open application window, when requests
come from people on a deadline instead of from a health probe. A variable
rather than a literal so that move is a deployment and not a pull request.
''')
@allowed(['0.25', '0.5', '0.75', '1.0', '1.5', '2.0'])
param containerSize string = '0.5'

// The known-good pairs. Container Apps' Consumption profile wants memory at
// exactly twice the CPU, so memory is derived here rather than asked for.
var containerSizes = {
  '0.25': { cpu: json('0.25'), memory: '0.5Gi' }
  '0.5': { cpu: json('0.5'), memory: '1Gi' }
  '0.75': { cpu: json('0.75'), memory: '1.5Gi' }
  '1.0': { cpu: json('1.0'), memory: '2Gi' }
  '1.5': { cpu: json('1.5'), memory: '3Gi' }
  '2.0': { cpu: json('2.0'), memory: '4Gi' }
}
var containerResources = containerSizes[containerSize]

@description('''
Shared secret proving a request reached harbor through one of our front ends.

Harbor has a public hostname, so the forwarded client address is only believed
when this arrives with it. Empty means never believed, which is a coarser rate
limit rather than an absent one.
''')
@secure()
param proxySecret string = ''

@description('''
The key the assistant's question reader uses. Empty leaves the word table in
charge, which answers the common phrasings without a network call -- so an
absent key is a quieter assistant, never a broken one.
''')
@secure()
param openAiApiKey string = ''

@description('Whether the applicant portal is served. Empty leaves features.json to decide.')
param enableHackerPortalFeature string = ''
param enableHackerDirectoryFeature string = ''

param tags object = {}

var suffix = 'mh-${environmentName}'

// The same expression platform.bicep builds the account from. Duplicated
// rather than passed, for the same reason the Postgres server is looked up by
// name here: apps.bicep is deployed on its own, after platform, and must not
// depend on that deployment's outputs still being available.
var resumeStorageAccount = 'stmh${environmentName}${uniqueString(subscription().subscriptionId, environmentName)}'

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
  scope: resourceGroup(registryResourceGroup)
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: 'cae-${suffix}'
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' existing = {
  name: 'psql-${suffix}'
}

// Empty means the Azure server next door, so an environment nobody has moved
// behaves exactly as it did. See platform.bicep for why this is one opaque
// string rather than a host parameter.
var azureConnectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=${dbName};Username=${dbAdminUser};Password=${dbPassword};SSL Mode=Require;Trust Server Certificate=true'
var connectionString = empty(appConnectionString) ? azureConnectionString : appConnectionString

// KEDA does not speak the string above.
//
// That is an Npgsql keyword string: semicolon-separated, 'SSL Mode' with a
// space, 'Trust Server Certificate'. KEDA's postgresql scaler hands its
// connection parameter to libpq, which wants space-separated lowercase keys
// and has no idea what 'SSL Mode' means. Passing the Npgsql string makes the
// scaler fail to connect, report no metric, and leave lark at zero replicas --
// so mail silently stops while the deployment, the app and every probe stay
// green. That is the exact failure the old hardcoded minReplicas: 1 existed to
// prevent, so it is worth a second rendering of the same credentials rather
// than a reused secret that looks interchangeable and is not.
//
// Which is also why moving the database needs its own third rendering rather
// than reusing appConnectionString: that one is Npgsql keyword syntax, and
// handing it to libpq fails in exactly the silent way described above. The
// override is a separate parameter so it has to be written in libpq's own
// syntax -- space-separated keywords, or the postgresql:// URI that libpq also
// accepts and that hosted providers tend to hand you by default.
//
// There is no way to derive one from the other here. Bicep cannot parse a
// connection string, and a secure parameter should not be picked apart in a
// template expression even if it could.
var azureKedaConnectionString = 'host=${postgres.properties.fullyQualifiedDomainName} port=5432 dbname=${dbName} user=${dbAdminUser} password=${dbPassword} sslmode=require'
var kedaConnectionString = empty(appKedaConnection) ? azureKedaConnectionString : appKedaConnection

// Both only exist when lark is allowed to sleep. Kept conditional so an
// environment that keeps lark warm carries neither a second copy of the
// password nor a scaler polling a query nothing reads.
var larkKedaSecrets = larkWarmReplicas == 0
  ? [ { name: 'db-keda', value: kedaConnectionString } ]
  : []

// The condition mirrors lark's own claim query, including next_attempt_at.
// Counting every 'pending' row instead would hold the worker awake for as long
// as one message sat in backoff -- which is both the opposite of the intent and
// the most expensive possible way to be wrong.
var larkScaleRules = larkWarmReplicas == 0 ? [
  {
    name: 'mail-is-waiting'
    custom: {
      type: 'postgresql'
      metadata: {
        query: 'SELECT count(*) FROM notify.messages WHERE status = \'pending\' AND (next_attempt_at IS NULL OR next_attempt_at <= now())'
        targetQueryValue: '1'
        activationTargetQueryValue: '0'
      }
      auth: [
        { secretRef: 'db-keda', triggerParameter: 'connection' }
      ]
    }
  }
] : []

var registryConfig = [
  {
    server: registry.properties.loginServer
    identity: pullIdentityId
  }
]

// Attached to every app and to the job, because a registry credential that is
// an identity has to be an identity the resource actually holds.
var pullIdentityConfig = {
  type: 'UserAssigned'
  userAssignedIdentities: {
    '${pullIdentityId}': {}
  }
}

var dbSecret = {
  name: 'db-connection'
  value: connectionString
}

// Container Apps rejects a secret with an empty value outright, so "Sentry is
// off until a DSN is set" has to mean the secret is absent rather than blank.
// Otherwise the thing that lets this run with no accounts is the thing that
// stops it deploying.
// The proxy secret, as a secret rather than a value.
//
// It was wired as a plain env entry while carrying @secure(), which meant
// anybody holding Reader on the resource group could read it back out of
// `az containerapp show` without the list-secrets permission that name
// implies. The value decides whether harbor believes a forwarded client
// address, so reading it is enough to forge one and walk past the per-IP
// limiter on sign-in.
//
// Absent rather than blank when unset, for the reason the Sentry block below
// gives: Container Apps refuses a secret with an empty value. ClientAddress
// treats a missing secret and an empty one the same way -- IsNullOrWhiteSpace,
// then trust nothing -- so dropping the variable entirely keeps the existing
// fail-closed behaviour rather than changing it.
var hasProxySecret = !empty(proxySecret)

var proxySecrets = hasProxySecret ? [
  {
    name: 'proxy-secret'
    value: proxySecret
  }
] : []

var proxyEnv = hasProxySecret ? [
  { name: 'Network__ProxySecret', secretRef: 'proxy-secret' }
] : []

// The assistant's provider key, as a secret rather than a value.
//
// Same treatment as the proxy secret above and for the same reason: a @secure()
// param wired as a plain env entry is readable out of `az containerapp show`
// by anybody holding Reader, without the list-secrets permission that name
// implies. This one is billable, so reading it is spending somebody's money.
//
// Absent rather than blank when unset, because Container Apps refuses a secret
// with an empty value -- and atlas reads a missing key the same way it reads
// an empty one, by keeping the word table.
var hasOpenAi = !empty(openAiApiKey)

var openAiSecrets = hasOpenAi ? [
  {
    name: 'openai-api-key'
    value: openAiApiKey
  }
] : []

var openAiEnv = hasOpenAi ? [
  { name: 'OpenAI__ApiKey', secretRef: 'openai-api-key' }
] : []

var hasSentry = !empty(sentryDsn)

var sentrySecrets = hasSentry ? [
  {
    name: 'sentry-dsn'
    value: sentryDsn
  }
] : []

// Same shape as Sentry: absent rather than blank, because Container Apps
// rejects a secret with an empty value.
var hasAws = !empty(awsRegion) && !empty(awsAccessKeyId)

var awsSecrets = hasAws ? [
  {
    name: 'aws-access-key-id'
    value: awsAccessKeyId
  }
  {
    name: 'aws-secret-access-key'
    value: awsSecretAccessKey
  }
] : []

var awsEnv = hasAws ? concat([
  { name: 'AWS_REGION', value: awsRegion }
  { name: 'AWS_ACCESS_KEY_ID', secretRef: 'aws-access-key-id' }
  { name: 'AWS_SECRET_ACCESS_KEY', secretRef: 'aws-secret-access-key' }
], empty(sesConfigurationSet) ? [] : [
  // Plain value rather than a secret: it is the name of a configuration set,
  // which is not one.
  { name: 'SES_CONFIGURATION_SET', value: sesConfigurationSet }
]) : []

// Absent rather than blank, like the others: Container Apps rejects a secret
// with an empty value.
var hasGoogle = !empty(googleClientId) && !empty(googleClientSecret)

var googleSecrets = hasGoogle ? [
  {
    name: 'google-client-secret'
    value: googleClientSecret
  }
] : []

var googleEnv = hasGoogle ? [
  { name: 'Google__ClientId', value: googleClientId }
  { name: 'Google__ClientSecret', secretRef: 'google-client-secret' }
  { name: 'Google__RedirectUri', value: googleRedirectUri }
] : []

// Plain rather than absent-when-empty, unlike the secrets above: this is not a
// secret, so it needs no Container Apps secret entry, and atlas has a working
// localhost default for it. An empty value is a misconfiguration worth seeing
// in the template rather than a deployment worth blocking.
var portalEnv = concat(
  empty(publicBaseUrl) ? [] : [
    { name: 'PublicBaseUrl', value: publicBaseUrl }
  ],
  empty(consoleBaseUrl) ? [] : [
    { name: 'ConsoleBaseUrl', value: consoleBaseUrl }
  ],
  empty(formsBaseUrl) ? [] : [
    { name: 'FormsBaseUrl', value: formsBaseUrl }
  ])

// Feature flags, as deployment inputs rather than hand-patched env vars.
//
// A flag set with `az containerapp update` survives exactly until the next
// deployment, because this template defines the container's environment and
// anything absent from it is removed. That is not a quirk to work around: an
// environment nobody can rebuild from the repository is one nobody can restore
// after an outage. So a flag that needs to differ per environment comes through
// here.
//
// Empty means absent, and absent means features.json decides. That three-state
// arrangement is the same one warmReplicas needs, and for the same reason: a
// variable that does not exist on a GitHub environment arrives as an empty
// string, which must not be read as "off" when "off" is also a real answer
// somebody might have chosen.
//
// Adding the next flag is this block again under another name.
var featureEnv = concat(empty(enableHackerPortalFeature) ? [] : [
  { name: 'ENABLE_HACKER_PORTAL_FEATURE', value: enableHackerPortalFeature }
], empty(enableHackerDirectoryFeature) ? [] : [
  { name: 'ENABLE_DIRECTORY_FEATURE', value: enableHackerDirectoryFeature }
])

// Where resumes go. No key and no connection string: the account name plus the
// identity is the whole configuration, which is the point of choosing an
// object store in the subscription we already own. There is nothing here that
// would be a secret if it leaked.
var resumeEnv = [
  { name: 'Resumes__AccountName', value: resumeStorageAccount }
  { name: 'Resumes__Container', value: 'resumes' }
  { name: 'Resumes__ClientId', value: pullIdentityClientId }
]

var sentryEnv = hasSentry ? [
  { name: 'Sentry__Dsn', secretRef: 'sentry-dsn' }
  // The deployed commit, so a spike in errors ties to what shipped rather
  // than being matched up by timestamp.
  { name: 'Sentry__Release', value: imageTag }
] : []

// ------------------------------------------------------------------ atlas ---
// Internal ingress. Harbor is the only path to the API. Atlas validates its
// own sessions and permissions rather than trusting the gateway, but there is
// still no reason to also publish it.
resource atlas 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-atlas-${environmentName}'
  location: location
  identity: pullIdentityConfig
  tags: union(tags, { service: 'atlas' })
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: false
        targetPort: 8080
        transport: 'auto'
      }
      registries: registryConfig
      secrets: concat([dbSecret], sentrySecrets, googleSecrets, proxySecrets, openAiSecrets)
    }
    template: {
      containers: [
        {
          name: 'atlas'
          image: '${registry.properties.loginServer}/atlas:${imageTag}'
          resources: containerResources
          env: concat([
            { name: 'ARCTIC_DB', secretRef: 'db-connection' }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            // Two proxies sit in front of atlas, not one: harbor, and then the
            // Container Apps internal ingress that fronts every app in the
            // environment. With one, atlas stops at the internal ingress and
            // records that instead of the caller — which is what made
            // sessions.ip read 100.100.x and put every request in a single
            // rate-limit bucket.
            { name: 'Network__ForwardLimit', value: '2' }
            // Liveness only, and deliberately not the database: a Postgres
            // blip that restarts every replica turns a recoverable problem
            // into an outage.
            { name: 'ASPNETCORE_ENVIRONMENT', value: environmentName == 'prod' ? 'Production' : 'Staging' }
          ], sentryEnv, googleEnv, portalEnv, resumeEnv, featureEnv, proxyEnv, openAiEnv)
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: { minReplicas: warmReplicas, maxReplicas: 3 }
    }
  }
}

// ------------------------------------------------------------------- lark ---
// No ingress at all. Zero replicas is permitted only because the scale rule
// above can wake it: a queue with no worker silently stops sending while every
// dashboard reads green, so the replica floor and the rule that replaces it
// have to arrive together or not at all.
resource lark 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-lark-${environmentName}'
  location: location
  identity: pullIdentityConfig
  tags: union(tags, { service: 'lark' })
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      registries: registryConfig
      secrets: concat([dbSecret], sentrySecrets, awsSecrets, larkKedaSecrets)
    }
    template: {
      containers: [
        {
          name: 'lark'
          image: '${registry.properties.loginServer}/lark:${imageTag}'
          resources: containerResources
          env: concat([
            { name: 'ARCTIC_DB', secretRef: 'db-connection' }
            { name: 'DOTNET_ENVIRONMENT', value: environmentName == 'prod' ? 'Production' : 'Staging' }
            { name: 'SendLoop__ClickTrackingBaseUrl', value: 'https://${harbor.properties.configuration.ingress.fqdn}/api' }
            { name: 'SendLoop__UnsubscribeBaseUrl', value: 'https://${harbor.properties.configuration.ingress.fqdn}/api' }
          ], sentryEnv, awsEnv)
        }
      ]
      scale: {
        minReplicas: larkWarmReplicas
        maxReplicas: 1
        rules: larkScaleRules
      }
    }
  }
}

// ----------------------------------------------------------------- harbor ---
// The only thing exposed to the internet.
resource harbor 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-harbor-${environmentName}'
  location: location
  identity: pullIdentityConfig
  tags: union(tags, { service: 'harbor' })
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      registries: registryConfig
      secrets: concat(sentrySecrets, proxySecrets)
    }
    template: {
      containers: [
        {
          name: 'harbor'
          image: '${registry.properties.loginServer}/harbor:${imageTag}'
          resources: containerResources
          env: concat([
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'ReverseProxy__Clusters__atlas__Destinations__primary__Address', value: 'https://${atlas.properties.configuration.ingress.fqdn}/' }
            { name: 'ASPNETCORE_ENVIRONMENT', value: environmentName == 'prod' ? 'Production' : 'Staging' }
            // Exactly one proxy sits in front of harbor: the Container Apps
            // ingress, which appends the real client to X-Forwarded-For. One
            // means we take that entry and stop.
            //
            // Two was a hole rather than a margin. A caller who sends their
            // own X-Forwarded-For has it appended to, not replaced, so
            // consuming a second entry reaches the value they chose — proven
            // against staging, where a forged header became the client IP and
            // made every per-IP rate limit trivially bypassable.
            { name: 'Network__ForwardLimit', value: '1' }
          ], sentryEnv, proxyEnv)
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/api/health', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: { minReplicas: warmReplicas, maxReplicas: 3 }
    }
  }
}

output harborFqdn string = harbor.properties.configuration.ingress.fqdn
