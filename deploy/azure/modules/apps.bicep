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

// Where the images come from. ghcr.io rather than a container registry of our
// own, because Basic ACR was the largest single line on the bill once the
// services started sleeping -- more than all six of them put together -- and
// ghcr.io is included with the repository that already builds them.
//
// The cost of that is this token. A managed identity cannot pull from ghcr.io,
// so there is a credential here where there was none, and it is the one thing
// in this template that has to be rotated by hand. Keep it to read:packages:
// it pulls images and must not be able to do anything else.
@description('Lowercase owner/repo on ghcr.io holding the images.')
param ghcrRepository string = 'morgan-hacks/arctic'

@description('GitHub user the pull token belongs to.')
param ghcrUsername string

@secure()
@description('GitHub token with read:packages and nothing more.')
param ghcrToken string

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

0.5 is the default because it is what these services have always run, and for
no stronger reason than that. This parameter was introduced with the claim that
a smaller size would lengthen cold starts badly, so the default had to be the
larger one. That claim was wrong, and measuring it is what showed it:

  0.5  vCPU   cold 18.5s   warm 0.10-0.12s
  0.25 vCPU   cold 19.2s   warm 0.16s

Six tenths of a second. A .NET cold start here is pulling a 95 MiB image and
initialising a runtime, and neither of those is waiting on the half vCPU it was
not given. Working set is nowhere near the limit either -- 159-171 MiB for
atlas and harbor, 272 MiB for lark -- so 0.25 is a safe size anywhere, not just
somewhere nobody is watching.

What remains true is that it hardly matters in either direction. Against a
replica floor of zero, sleeping is the whole saving and the size is a rounding
error: at the awake time these services actually accumulate, 0.25 costs nothing
a month and 0.5 costs about 65 cents. The default stays 0.5 because changing it
would redeploy production to save eight dollars a year, which is not a reason to
touch production.

So: set CONTAINER_SIZE=0.25 wherever you like -- the measurement says it is
fine. And raise it -- 0.75, 1.0 -- for the weeks around an open application
window, when requests come from people on a deadline instead of from a health
probe. That is the direction this parameter actually exists for, and a variable
rather than a literal so the move is a deployment and not a pull request.
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

// The condition mirrors lark's own claim query, including next_attempt_at and
// the suppression check. Counting every 'pending' row instead would hold the
// worker awake for as long as one message sat in backoff -- which is both the
// opposite of the intent and the most expensive possible way to be wrong.
//
// The suppression half of that mirror is not optional, because a suppressed row
// is not in backoff: it is never claimable again. MessageQueue.SuppressAsync
// sweeps the rows already queued when an address is suppressed, but nothing
// sweeps a row queued to an address that was suppressed earlier -- the claim
// just skips it, and its own remarks say that leaves it pending forever. One
// such row counts as work here while being work lark cannot take, so the scaler
// would wake it, find nothing to claim, and hold it at one replica for good.
// That is the whole point of letting it sleep, lost to a single bounced address
// that someone later mailed again.
var larkScaleRules = larkWarmReplicas == 0 ? [
  {
    name: 'mail-is-waiting'
    custom: {
      type: 'postgresql'
      metadata: {
        query: 'SELECT count(*) FROM notify.messages m WHERE m.status = \'pending\' AND (m.next_attempt_at IS NULL OR m.next_attempt_at <= now()) AND NOT EXISTS (SELECT 1 FROM notify.suppressions s WHERE s.email = m.to_email AND (s.reason <> \'unsubscribed\' OR m.priority > 0))'
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
    server: 'ghcr.io'
    username: ghcrUsername
    passwordSecretRef: 'ghcr-token'
  }
]

// Every app that pulls an image needs its own copy, because a
// passwordSecretRef resolves against the secrets of the app it is written on.
var ghcrSecrets = [
  {
    name: 'ghcr-token'
    value: ghcrToken
  }
]

// Still attached to every app, and no longer for the registry: the identity
// reads applicant resumes out of blob storage, which is why it outlived the
// AcrPull grant it was first created for.
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
      secrets: concat([dbSecret], ghcrSecrets, sentrySecrets, googleSecrets, proxySecrets, openAiSecrets)
    }
    template: {
      containers: [
        {
          name: 'atlas'
          image: 'ghcr.io/${ghcrRepository}/atlas:${imageTag}'
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
            // Where to knock after queueing mail, composed by convention
            // rather than read off lark.
            //
            // lark.properties.configuration.ingress.fqdn would be the obvious
            // way to write this and it does not compile: harbor already reads
            // atlas's FQDN and lark already reads harbor's, so atlas reading
            // lark's closes the loop and Bicep refuses the cycle. The internal
            // name is deterministic -- <app>.internal.<environment domain> --
            // so taking it from the environment instead breaks the cycle
            // without inventing a parameter somebody has to keep in sync.
            //
            // Absent in local development, where there is no environment to
            // compose a name from. QueuedEmailSender treats that as "do not
            // knock" rather than as an error, because the scale rule still
            // picks the message up.
            { name: 'LARK_WAKE_URL', value: 'https://ca-lark-${environmentName}.internal.${environment.properties.defaultDomain}/api/wake' }
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
// A newer API version than its two neighbours, for one property.
//
// scale.pollingInterval is not in 2024-03-01's type definitions. Bicep still
// renders it -- it is only a BCP037 warning -- and ARM would accept the
// template, but whether the provider honours a property its declared contract
// does not mention is not something to find out from a bill. A deploy that
// goes green while the scaler keeps polling every thirty seconds is the exact
// shape of failure this change exists to remove.
//
// 2025-01-01 is the earliest stable version that declares it, so the
// difference is one minor version and the warning is gone rather than ignored.
resource lark 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'ca-lark-${environmentName}'
  location: location
  identity: pullIdentityConfig
  tags: union(tags, { service: 'lark' })
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      registries: registryConfig
      secrets: concat([dbSecret], ghcrSecrets, sentrySecrets, awsSecrets, larkKedaSecrets)
      // Internal, and only so that something can knock.
      //
      // lark answers one request and ignores what it says: being started is
      // the entire point. SendLoop is a BackgroundService, so it begins
      // draining the queue the moment the process exists, and Container Apps'
      // own HTTP scaler brings a sleeping replica up to serve the request.
      // That replaces a 30-second database poll with a knock that arrives when
      // there is actually something to send.
      //
      // internal: there is no reason for this to be reachable from outside the
      // environment, and the only caller is atlas.
      ingress: {
        external: false
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'lark'
          image: 'ghcr.io/${ghcrRepository}/lark:${imageTag}'
          resources: containerResources
          env: concat([
            { name: 'ARCTIC_DB', secretRef: 'db-connection' }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'DOTNET_ENVIRONMENT', value: environmentName == 'prod' ? 'Production' : 'Staging' }
            { name: 'SendLoop__ClickTrackingBaseUrl', value: 'https://${harbor.properties.configuration.ingress.fqdn}/api' }
            { name: 'SendLoop__UnsubscribeBaseUrl', value: 'https://${harbor.properties.configuration.ingress.fqdn}/api' }
          ], sentryEnv, awsEnv)
        }
      ]
      scale: {
        minReplicas: larkWarmReplicas
        maxReplicas: 1
        // An hour, where this used to be thirty seconds.
        //
        // The scaler is no longer how mail gets noticed -- atlas knocks on the
        // ingress above the moment it enqueues -- so this only has to catch
        // what nobody knocked about: a send that failed and set
        // next_attempt_at, or a row written by something that cannot knock.
        // Those can wait an hour; a magic link cannot, and no longer does.
        //
        // The interval is also what the database bill is made of. The query
        // runs whether or not lark is awake, because that is how the scaler
        // decides to wake it -- so on a provider that suspends an idle
        // database, a 30-second poll is a heartbeat that keeps it running
        // every minute of every day. Measured against Neon's five-minute idle
        // cutoff: 30s polling costs 182 compute-hours a month against a
        // hundred free, and hourly costs 15.
        pollingInterval: 3600
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
      secrets: concat(ghcrSecrets, sentrySecrets, proxySecrets)
    }
    template: {
      containers: [
        {
          name: 'harbor'
          image: 'ghcr.io/${ghcrRepository}/harbor:${imageTag}'
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
