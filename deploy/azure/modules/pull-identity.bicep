// The identity the services read blob storage with.
//
// Created to pull images from the shared ACR, which is where the name comes
// from. The images come from ghcr.io now and a managed identity cannot pull
// from there, so the registry half of its job is gone and the storage half is
// not: atlas reads applicant resumes through this identity, so it is still
// attached to every app. Renaming it would mean recreating it and reissuing
// every role assignment, which is not worth the tidiness.

targetScope = 'resourceGroup'

param environmentName string
param location string = resourceGroup().location
param tags object = {}

resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-mh-${environmentName}-pull'
  location: location
  tags: tags
}

output id string = pullIdentity.id
output principalId string = pullIdentity.properties.principalId

// Needed by anything that authenticates *as* this identity from inside a
// container, rather than merely being assigned it. A container app with more
// than one identity available has to be told which to use, and the Azure SDK
// asks for the client id — with nothing set it picks the system-assigned one,
// which holds no roles, and every call fails as if the role assignment were
// missing.
output clientId string = pullIdentity.properties.clientId
