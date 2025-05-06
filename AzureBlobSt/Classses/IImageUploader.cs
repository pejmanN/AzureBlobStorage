
namespace AzureBlobSt.Classses
{
    public interface IImageUploader
    {
        Task<ImageUploadResult> UploadImageAsync(string blobContainerName, IFormFile file);
    }
}