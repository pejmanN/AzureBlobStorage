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
docker build -t azureblobapp:1.0.2 .
az acr login --name $azureContainerRegistryName
$azureContainerRegistryAddress=$(az acr show --name $azureContainerRegistryName --query "loginServer" --output tsv)
docker tag azureblobapp:1.0.2 "$azureContainerRegistryAddress/azureblobapp:1.0.2"

docker push "$azureContainerRegistryAddress/azureblobapp:1.0.2"

```

Configure AKS:
```
$orderAzureKuberName = "orderazurekuber"
az aks create -n $orderAzureKuberName -g $serviceGroupName --node-vm-size Standard_B2s --node-count 2 --attach-acr $azureContainerRegistryName --enable-oidc-issuer --enable-workload-identity --generate-ssh-keys --location $resourceGroupLocation

az aks get-credentials --name $orderAzureKuberName --resource-group $serviceGroupName

```

Create Namespace and apply deployment and services:
```
$namespace="azureblobapp"
kubectl create namespace $namespace

kubectl apply -f .\application.yaml -n $namespace
kubectl apply -f .\service.yaml -n $namespace
kubectl apply -f .\serviceaccount.yaml -n $namespace
```


ManageIdentity and Federation:
```
$azureManageIdentityForIdentityMicroserviceName = "AzureKeyVaultServiceManageIdentity"
az identity create --name $azureManageIdentityForIdentityMicroserviceName --resource-group $serviceGroupName

$MANAGE_IDENTITY_CLIENT_ID = az identity show -g $serviceGroupName -n $azureManageIdentityForIdentityMicroserviceName --query clientId -o tsv

$AKS_OIDC_ISSUER=az aks show -n $orderAzureKuberName -g  $serviceGroupName --query "oidcIssuerProfile.issuerUrl" -o tsv

$federateCredentialName="idenityservicefederatedidcredential"
az identity federated-credential create --name $federateCredentialName --identity-name $azureManageIdentityForIdentityMicroserviceName --resource-group $serviceGroupName --issuer $AKS_OIDC_ISSUER --subject "system:serviceaccount:${namespace}:azureblobapp-serviceaccount"

```


## Step 3
Preparing Azure Storage Blob

✅ Azure Storage Account vs Azure Storage
Term	Meaning
Azure Storage =>	The overall service that includes multiple storage options: Blob, File, Queue, Table, and Disk.
Azure Storage Account =>A container or namespace in Azure that gives you access to those storage types. You must create a Storage Account first to use any Azure Storage services.

🔧 Example:
    Azure Storage = The whole building

    Storage Account = One apartment unit you rent

    Blob Container = A closet inside your apartment

    Blob (Image/File) = The actual item you put in the closet

    So yes, a "Storage Account" is your entry point to using Azure Storage services.


Create the Storage Account:
```
$storageAccountName="orderblobstorage01"
az storage account create  --name $storageAccountName  --resource-group $serviceGroupName --location $resourceGroupLocation  --sku Standard_LRS  --kind StorageV2  --allow-blob-public-access true

```
NOTE=> Maybe in running above command u got error that the Microsoft.Storage resource provider wasn't properly registered in your Azure subscription., u can run:
```
az provider register --namespace Microsoft.Storage
```

Assign RBAC Role to Managed Identity
```
az role assignment create `
   --assignee $MANAGE_IDENTITY_CLIENT_ID `
   --role "Storage Blob Data Owner" `
   --scope "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$serviceGroupName/providers/Microsoft.Storage/storageAccounts/$storageAccountName"

```

Update Your App Configuration
```
env:
  - name: BlobSettings__Url
    value: https://orderblobstorage01.blob.core.windows.net

```

For Testing:
```
http://20.253.171.31/api/image => POST

https://orderblobstorage01.blob.core.windows.net/images/EASportsFC25.jpg  => GET
```


## Step 4
✅ Azure Front Door Setup (Performance + Global Routing)

💠 Two Ways to Integrate Front Door with Blob Storage
✅ 1. Portal-based approach (what you saw in the tutorial):
    You go to your Storage Account > Front Door and CDN > choose a Front Door profile or create one.

    Azure automatically connects your blob endpoint (e.g., https://mystorage.blob.core.windows.net) as a backend for the Front Door.

    Good for static websites or serving public blobs with performance enhancements (CDN, caching, WAF, etc.).

✅ 2. CLI or Bicep/Terraform approach (Infrastructure as Code):
    You use az network front-door ... or az afd ... commands.

    You manually define the:

        Front Door profile

        Endpoint

        Origin (your blob endpoint)

        Origin group

        Route (to map requests to your origin)

This method gives full control and works well in DevOps pipelines, production environments, and non-public storage (secured behind Private Endpoints or signed URLs).

❓Are they the same?
✅ Goal: Yes — both connect Azure Front Door to your Blob Storage to deliver files faster and securely.

⚙️ Implementation: Different.

        The portal approach does some automation for you behind the scenes.

        The CLI/IaC approach is more explicit and gives you more control.




✅ 1. Create Front Door Profile (Standard SKU, WAF not attached)
```
$fdProfileName = "orderfdprofile"
az afd profile  create --name $fdProfileName --resource-group $serviceGroupName --sku Standard_AzureFrontDoor

```
✅ 2. Create Origin Group
```
$originGroupName = "bloboriginGroup"
az afd origin-group create `
  --resource-group $serviceGroupName `
  --profile-name $fdProfileName `
  --origin-group-name $originGroupName `
  --probe-request-type GET `
  --probe-protocol Https `
  --probe-path "/" `
  --sample-size 4 `
  --successful-samples-required 3 `
  --additional-latency 50

```
--probe-request-type GET	This tells AFD to use HTTP GET requests to probe (health check) the origins in the group.
--probe-protocol Https	Specifies that health probes should use HTTPS (not HTTP).
--probe-path "/"	Sets the URL path for the health probe requests. In this case, it’s /, which means AFD will send probe requests to the root path of your backend.


✅ 3. Add Blob Storage as Origin
```
$originName = "azurestroageorigin"
az afd origin create   --resource-group $serviceGroupName   --profile-name $fdProfileName   --origin-group-name $originGroupName   --name $originName `
  --host-name "$storageAccountName.blob.core.windows.net"   --origin-host-header "$storageAccountName.blob.core.windows.net"   --http-port 80   --https-port 443
```

✅ 4. Create Frontend Endpoint
```
$frontendEndpointName ="blobfrontend"
az afd endpoint create   --resource-group $serviceGroupName   --profile-name $fdProfileName   --name $frontendEndpointName

```

✅ 5. Create Route (with Caching + UseQueryString behavior)
```
$routeName = "blobrouting"

az afd route create   --resource-group $serviceGroupName   --profile-name $fdProfileName   --endpoint-name $frontendEndpointName   --origin-group $originGroupName  ` --name $routeName   --route-type Forward   --https-redirect Enabled   --patterns "/*"   --supported-protocols Https   --cache-enabled true `
  --query-parameter-strip none   --forwarding-protocol MatchRequest

```
🔥 --cache-enabled true: enables Azure Front Door caching
🔧 --query-parameter-strip none: this means query strings are preserved (i.e., “Use query string”)
🚫 No --waf-policy-id: so WAF is not enabled


For Testing :
```
https://blobfrontend-a7cafse7brhgdhfz.z02.azurefd.net/images/BlackMythWukong.jpg
```


********************************* NOTE ***************************************
🔎 So How Does Azure Front Door Route Requests Within an Origin Group?
When you define a Route with:
```
--origin-group $originGroupName
--patterns "/*"
```
This means:

    Any request matching /* (i.e. any path) will be forwarded to that origin group.

    Now, the origin group may have one or more origins inside it (e.g., multiple blob accounts, a web app, etc.).

    Front Door selects the right origin from the group using load balancing and health probe results — unless you manually override it with priorities or weights.


🔁 If Only One Origin Exists (like your case):
If the origin group has just one origin — e.g., only your blob storage — then Front Door always sends traffic to that origin, no decision-making is needed.


⚙️ If Multiple Origins Exist:
Front Door chooses based on:

    Setting	Description
    --priority	Lower number = higher priority. If priority 1 is healthy, traffic never goes to priority 2.
    --weight	Load balancing between origins with same priority.
    Health probe	If an origin fails health checks, it's removed temporarily from routing.

You can control this when calling:
```
az network front-door origin create \
  --priority 1 \
  --weight 100

```


🔹  What Happens When a User Sends a Request?

Let’s walk through a real scenario:

📦 Scenario:
User opens the URL:
```
https://blobfrontend.azurefd.net/images/photo.jpg?size=large

```

🔁 Request Journey:
1. Client DNS Resolution

  . User’s browser resolves blobfrontend.azurefd.net to an IP managed by Azure Front Door.

2. Front Door Ingress

  . Front Door receives the request on the frontend endpoint.

  . Applies HTTPS redirect (if user typed http).


3. Routing Decision

  . The route (blobrouting) is triggered because the path /* matches.

  . Route is configured to:

    . Keep query string ?size=large

    . Use caching (checks if file is already cached)

    . Forward to the origin group if not cached


4. Origin Group Processing

    . Origin group picks the origin (orderblobstorage.blob.core.windows.net) that’s healthy.

    . Sends the request to:
```
https://orderblobstorage.blob.core.windows.net/images/photo.jpg?size=large

```


5. Response Flow

    . If image is found, it is returned to Front Door.

    . Front Door caches the response (for future requests).

    . Then it returns it to the end user.
