## Step1
Install nuget `Azure.Storage.Blobs` and `Azure.Identity` and define `ImageUploader` class.

🧾 PublicAccessType Enum:
It determines who can access blobs or containers without authentication:

Value	Description
None	No public access. Only authorized requests (with credentials or SAS tokens) can access blobs or container metadata. ✅ Recommended for private data.
Blob	Allows anonymous read access to individual blobs (e.g., .jpg files) but not to container metadata or the list of blobs. ❗Use carefully.
Container	Allows anonymous read access to both blobs and container metadata (e.g., listing all files). ❗❗Very open.


## Step2
Preparing the Azure Resources

```
$serviceGroupName = "orderRG"
$resourceGroupLocation = "westus"
az group create --name $serviceGroupName --location $resourceGroupLocation

$azureContainerRegistryName = "ordercontainerregistry"
az acr create --name $azureContainerRegistryName --resource-group $serviceGroupName --sku Basic
```

build Image and push it to ACR
```
docker build -t azureblobapp:1.0.1 .
az acr login --name $azureContainerRegistryName
$azureContainerRegistryAddress=$(az acr show --name $azureContainerRegistryName --query "loginServer" --output tsv)
docker tag azureblobapp:1.0.1 "$azureContainerRegistryAddress/azureblobapp:1.0.1"

docker push "$azureContainerRegistryAddress/azureblobapp:1.0.1"

```

Configure AKS:
```
$orderAzureKuberName = "orderazurekuber"
az aks create -n $orderAzureKuberName -g $serviceGroupName --node-vm-size Standard_B2s --node-count 2 --attach-acr $azureContainerRegistryName --enable-oidc-issuer --enable-workload-identity --generate-ssh-keys --location $resourceGroupLocation

az aks get-credentials --name $orderAzureKuberName --resource-group $serviceGroupName

```

Create Namespace and apply deployment and services:
```
$namespace="keyvaultapp"
kubectl create namespace $namespace

kubectl apply -f .\application.yaml -n $namespace
kubectl apply -f .\service.yaml -n $namespace
kubectl apply -f .\serviceaccount.yaml -n $namespace
```


ManageIdentity and Federation:
```
$azureManageIdentityForIdentityMicroserviceName = "AzureKeyVaultServiceManageIdentity"
az identity create --name $azureManageIdentityForIdentityMicroserviceName --resource-group $serviceGroupName

$MANAGE_IDENTITY_CLIENT_ID = az identity show -g $serviceGroupName -n $azureManageIdentityForIdenitytMicroserviceName --query clientId -o tsv

$AKS_OIDC_ISSUER=az aks show -n $orderAzureKuberName -g  $serviceGroupName --query "oidcIssuerProfile.issuerUrl" -o tsv

$federateCredentialName="idenityservicefederatedidcredential"
az identity federated-credential create --name $federateCredentialName --identity-name $azureManageIdentityForIdenitytMicroserviceName --resource-group $serviceGroupName --issuer $AKS_OIDC_ISSUER --subject "system:serviceaccount:${namespace}:azureblobapp-serviceaccount"

```

