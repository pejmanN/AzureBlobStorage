using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using AzureBlobSt.Classses;

namespace AzureBlobSt.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ImageController : ControllerBase
    {
        private readonly IImageUploader _imageUploader;
        private readonly string _containerName = "images";

        public ImageController(IImageUploader imageUploader)
        {
            _imageUploader = imageUploader;
        }

        [HttpPost]
        public async Task<IActionResult> UploadImage(IFormFile file)
        {
            var result = await _imageUploader.UploadImageAsync(_containerName, file);
            if (!result.IsSucess)
            {
                return BadRequest(result.Message);
            }

            return Ok("Uploaded successfully"); 
        }
    }
}
