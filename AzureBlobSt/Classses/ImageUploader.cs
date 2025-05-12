using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace AzureBlobSt.Classses
{
    public class ImageUploader : IImageUploader
    {
        private readonly BlobServiceClient _blobServiceClient;
        public ImageUploader(BlobServiceClient blobServiceClient)
        {
            _blobServiceClient = blobServiceClient;
        }

        public async Task<ImageUploadResult> UploadImageAsync(string blobContainerName, IFormFile file)
        {
            //Do ur validations like fileExtenstion and size ...
            if (file == null || file.Length == 0)
                return new ImageUploadResult { IsSucess = false, Message = "No file uploaded" };


            var containerClient = _blobServiceClient.GetBlobContainerClient(blobContainerName);
            await containerClient.CreateIfNotExistsAsync();
            await containerClient.SetAccessPolicyAsync(PublicAccessType.Blob);

            var blobClient = containerClient.GetBlobClient(file.FileName);
            await blobClient.DeleteIfExistsAsync();

            using var fileStream = file.OpenReadStream();
            await blobClient.UploadAsync(fileStream, new BlobHttpHeaders { ContentType = file.ContentType });

            return new ImageUploadResult
            {
                IsSucess = true,
                FileAddress = blobClient.Uri.ToString(),
            };
        }
    }
}
