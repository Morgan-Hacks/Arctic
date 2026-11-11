// The front door. Everything is reached from here.
//
// Subscription-scoped, so it owns the resource groups too — an environment is
// then one thing that either exists or does not, rather than a group somebody
// has to remember to create first.
//
//   az deployment sub create -l centralus -f main.bicep -p staging.bicepparam
//
// Deployed in two passes, and that is deliberate. `deployApps` is false on the
// first pass so the migration job can run against the new schema before any
// service that expects it is updated. One pass would put new code in front of
// an old schema for however long migrations take, and that window is where
// every migration bug lives. deploy.sh handles the sequence.

targetScope = 'subscription'

@allowed(['staging', 'prod'])
param environmentName string

// centralus rather than eastus: eastus is capacity-restricted for this
// subscription, and Postgres provisioning is refused there outright. centralus
// also offers Postgres 18, which is what docker-compose and the tests run.
param location string = 'centralus'

@description('Commit sha. Never "latest" — a rollback has to be a tag that already exists.')
param imageTag string

// The images come from ghcr.io, which belongs to the repository that builds
// them, so there is no registry here any more and nothing shared between the
// environments. See apps.bicep for what that trades away.
//
// The registry and its resource group are deliberately not deleted by removing
// them from this template -- Bicep deploys incrementally, so both survive and
// still hold every image a rollback might want. Deleting them is a separate,
// deliberate act once ghcr.io has been trusted for a while.
@description('Lowercase owner/repo on ghcr.io holding the images.')
param ghcrRepository string = 'morgan-hacks/arctic'

@description('GitHub user the pull token belongs to.')
param ghcrUsername string

@secure()
@description('GitHub token with read:packages and nothing more.')
param ghcrToken string

@secure()
param dbPassword string

@description('''
A whole connection string for the database the services should use, when that
is not the Azure server this deployment provisions. Empty means that server.

See platform.bicep for why this is an opaque string and not a host, and
apps.bicep for why the pooled and direct forms are separate values.
''')
@secure()
param appConnectionString string = ''

@description('The same database reached without a connection pooler, for migrations and dumps. Empty falls back to appConnectionString.')
@secure()
param appConnectionStringDirect string = ''

@description('The same database in libpq syntax, for the KEDA scaler that wakes lark. Empty means the Azure server. See apps.bicep.')
@secure()
param appKedaConnection string = ''

param superAdminEmail string

@secure()
@description('Empty disables error reporting, which is what lets this run with no accounts.')
param sentryDsn string = ''

@description('SES region. Empty means lark runs but sends nothing.')
param awsRegion string = ''

// Which SES configuration set sends are tagged with, and therefore whether SES
// reports bounces and complaints back. Empty degrades to the old behaviour.
param sesConfigurationSet string = ''

@secure()
param awsAccessKeyId string = ''

@secure()
param awsSecretAccessKey string = ''

@description('Google OAuth client id. Empty leaves organizer sign-in answering 503.')
param googleClientId string = ''

@secure()
param googleClientSecret string = ''

@description('Where Google sends the browser back. The admin app origin, not harbor.')
param googleRedirectUri string = ''

@description('''
Where applicants are, as opposed to where the API is: the portalweb origin.
Emailed sign-in links are built from this, so an empty or wrong value sends
every hacker to a link that goes nowhere.
''')
param publicBaseUrl string = ''

@description('''
Where the organizer console is: the portaladmin origin. A new organizer's
welcome email links to it, and an empty value means that email links nowhere.
Not the same as googleRedirectUri, which is this origin plus a callback path.
''')
param consoleBaseUrl string = ''

@description('''
Where the public forms site is: the portalforms origin, which is not the
portal's. A sign-in link for a form has to land on the host the form is served
from, because the session cookie is host-only — landing on the portal instead
sets a cookie the forms site is never sent, and the person arrives at the form
still signed out.
''')
param formsBaseUrl string = ''

@description('Postgres major version. Keep this the same as docker-compose and the tests.')
param postgresVersion string = '18'

@description('''
Shared secret the front ends send so their forwarded client address is
believed. Without it every caller is bucketed as Vercel, which is a coarser
rate limit rather than none.
''')
@secure()
param proxySecret string = ''

@secure()
param openAiApiKey string = ''

@description('''
Replicas of the web-facing services kept warm rather than asleep.

One keeps the API and gateway ready between requests. Set zero explicitly
when lower idle hosting usage is more important than cold-start latency.
''')
@minValue(0)
@maxValue(3)
param warmReplicas int = 1

@description('How many replicas of the mail worker to keep running. See apps.bicep; zero is only safe with the scale rule that module adds.')
@minValue(0)
@maxValue(1)
param larkWarmReplicas int = 1

@description('CPU and memory per replica, as one choice. See apps.bicep for why it is a size rather than two numbers, why the default is not the cheapest option, and the measurements behind it.')
@allowed(['0.25', '0.5', '0.75', '1.0', '1.5', '2.0'])
param containerSize string = '0.5'

@description('Whether the applicant portal is served. Empty leaves features.json to decide.')
param enableHackerPortalFeature string = ''
param enableHackerDirectoryFeature string = ''

@description('''
False on the first pass. The images have to be pushed before the migration job
can be created — Container Apps validates that the image is really there — so
pass one creates the group and the identity and stops.
''')
param deployPlatform bool = true

@description('''
False until migrations have run. Otherwise the services are updated in the same
deployment as the job, which puts new code in front of an old schema for
however long that job takes.
''')
param deployApps bool = true

// Azure's own abbreviations rather than something invented here, so a group
// reads by type and anyone who has used Azure before needs no explanation.
// Conventions are in naming.md.
var groupName = 'rg-mh-${environmentName}'

// On every resource. Cost Analysis groups by these, and next year's team
// inherits a subscription where "what is this and can I delete it" has an
// answer rather than being a guess.
var commonTags = {
  workload: 'mh'
  environment: environmentName
  managedBy: 'bicep'
  repository: 'Morgan-Hacks/Arctic'
}

resource group 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: groupName
  location: location
  tags: commonTags
}

// Created for AcrPull and kept for blob storage: atlas reads applicant resumes
// through it, so it outlived the registry it was named for.
module pullIdentity 'modules/pull-identity.bicep' = {
  name: 'pull-identity'
  scope: group
  params: {
    environmentName: environmentName
    location: location
    tags: commonTags
  }
}

module platform 'modules/platform.bicep' = if (deployPlatform) {
  name: 'platform'
  scope: group
  params: {
    environmentName: environmentName
    location: location
    imageTag: imageTag
    ghcrRepository: ghcrRepository
    ghcrUsername: ghcrUsername
    ghcrToken: ghcrToken
    dbPassword: dbPassword
    appConnectionString: appConnectionString
    appConnectionStringDirect: appConnectionStringDirect
    postgresVersion: postgresVersion
    superAdminEmail: superAdminEmail
    pullIdentityId: pullIdentity.outputs.id
    pullIdentityPrincipalId: pullIdentity.outputs.principalId
    tags: commonTags
  }
}

module apps 'modules/apps.bicep' = if (deployApps) {
  name: 'apps'
  scope: group
  params: {
    environmentName: environmentName
    location: location
    imageTag: imageTag
    ghcrRepository: ghcrRepository
    ghcrUsername: ghcrUsername
    ghcrToken: ghcrToken
    dbPassword: dbPassword
    appConnectionString: appConnectionString
    appKedaConnection: appKedaConnection
    sentryDsn: sentryDsn
    awsRegion: awsRegion
    sesConfigurationSet: sesConfigurationSet
    awsAccessKeyId: awsAccessKeyId
    awsSecretAccessKey: awsSecretAccessKey
    googleClientId: googleClientId
    googleClientSecret: googleClientSecret
    googleRedirectUri: googleRedirectUri
    publicBaseUrl: publicBaseUrl
    consoleBaseUrl: consoleBaseUrl
    formsBaseUrl: formsBaseUrl
    warmReplicas: warmReplicas
    larkWarmReplicas: larkWarmReplicas
    containerSize: containerSize
    enableHackerPortalFeature: enableHackerPortalFeature
    enableHackerDirectoryFeature: enableHackerDirectoryFeature
    proxySecret: proxySecret
    openAiApiKey: openAiApiKey
    pullIdentityId: pullIdentity.outputs.id
    pullIdentityClientId: pullIdentity.outputs.clientId
    tags: commonTags
  }
  dependsOn: [platform]
}

output postgresHost string = deployPlatform ? platform!.outputs.postgresHost : ''
output harborFqdn string = deployApps ? apps!.outputs.harborFqdn : ''
