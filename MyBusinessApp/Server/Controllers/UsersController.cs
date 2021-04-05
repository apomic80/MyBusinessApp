using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MyBusinessApp.Server.Data;
using MyBusinessApp.Shared;

namespace MyBusinessApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly MyBusinessAppDbContext _context;
        private readonly BlobContainerClient _blobContainerClient;
        private readonly ComputerVisionClient _computerVisionClient;

        public UsersController(
            MyBusinessAppDbContext context,
            IConfiguration configuration)
        {
            _context = context;
            _blobContainerClient = new BlobContainerClient(
                configuration.GetConnectionString("BlobConnectioString"),
                "photos");

            _computerVisionClient = new ComputerVisionClient(
                new ApiKeyServiceClientCredentials(
                    configuration["ComputerVisionKey"]))
            {
                Endpoint = configuration["CognitiveServicesEndpoint"]
            };
        }

        // GET: api/Users
        [HttpGet]
        public async Task<ActionResult<IEnumerable<User>>> GetUsers()
        {
            return await _context.Users.ToListAsync();
        }

        // GET: api/Users/5
        [HttpGet("{id}")]
        public async Task<ActionResult<User>> GetUser(int id)
        {
            var user = await _context.Users.FindAsync(id);

            if (user == null)
            {
                return NotFound();
            }

            return user;
        }

        // PUT: api/Users/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutUser(int id, User user)
        {
            if (id != user.Id)
            {
                return BadRequest();
            }

            _context.Entry(user).State = EntityState.Modified;

            try
            {
                var imageAnalysis = await AnalyzePhoto(user.PhotoContent);
                if (imageAnalysis.Faces == null || imageAnalysis.Faces.Count == 0) return BadRequest("Photo not valid!");
                user.Photo = await UploadPhoto(user.Photo, user.PhotoContent);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!UserExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/Users
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<User>> PostUser(User user)
        {
            var imageAnalysis = await AnalyzePhoto(user.PhotoContent);
            if (imageAnalysis.Faces == null || imageAnalysis.Faces.Count == 0) return BadRequest("Photo not valid!");

            user.Photo = await UploadPhoto(user.Photo, user.PhotoContent);
            _context.Users.Add(user);
            
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetUser", new { id = user.Id }, user);
        }

        [HttpPost("extractuserdata")]
        public async Task<ActionResult<User>> ExtractUserData(byte[] image)
        {
            var imageAnalysis = await AnalyzePhoto(image);
            var userPhoto = ExtractUserPhoto(imageAnalysis, image);
            var textList = await ExtractText(image);

            var labelFirstName = textList.FirstOrDefault(x => x.Item1.StartsWith("NOME"));
            var labelLastName = textList.FirstOrDefault(x => x.Item1.StartsWith("COGNOME"));

            var user = new User()
            {
                PhotoContent = userPhoto,
                FirstName = labelFirstName != null ? GetNearestLabel(textList, labelFirstName) : "",
                LastName = labelLastName != null ? GetNearestLabel(textList, labelLastName) : ""
            };

            return Ok(user);
        }

        private async Task<IList<Tuple<string, IList<double>>>> ExtractText(byte[] image)
        {
            using var imageStream = new MemoryStream(image);
            OcrResult result = await _computerVisionClient.RecognizePrintedTextInStreamAsync(true, imageStream);

            IList<Tuple<string, IList<double>>> textList = new List<Tuple<string, IList<double>>>();
            foreach (var region in result.Regions)
            {
                foreach (var line in region.Lines)
                {
                    foreach (var word in line.Words)
                    {
                        textList.Add( 
                            new Tuple<string, IList<double>>(
                                word.Text.Trim().ToUpper(),
                                word.BoundingBox.Split(",")
                                    .Select(x => Convert.ToDouble(x)).ToList())
                        );
                    }
                }
            }
            return textList;
        }

        private string GetNearestLabel(IList<Tuple<string, IList<double>>> textList, Tuple<string, IList<double>> label)
        {
            return textList.Select(x => new { 
                    d = Math.Pow(x.Item2[0] - label.Item2[0], 2) + Math.Pow(x.Item2[1] - label.Item2[1], 2),
                    Text = x.Item1
                })
                .Where(x => x.d > 0)
                .OrderBy(x => x.d)
                .FirstOrDefault()?.Text;
        }

        private byte[] ExtractUserPhoto(ImageAnalysis imageAnalysis, byte[] image)
        {
            if (imageAnalysis.Objects.Count > 0 && imageAnalysis.Objects[0].ObjectProperty == "person")
            {
                var x = imageAnalysis.Objects[0].Rectangle.X;
                var y = imageAnalysis.Objects[0].Rectangle.Y;
                var width = imageAnalysis.Objects[0].Rectangle.W;
                var height = imageAnalysis.Objects[0].Rectangle.H;

                using var imageStream = new MemoryStream(image);
                Image originalPhoto = Image.FromStream(imageStream, true, true) as Bitmap;
                Image extractedPhoto = new Bitmap(width, height);
                
                using var g = Graphics.FromImage(extractedPhoto);
                g.DrawImage(originalPhoto,
                    new Rectangle(0, 0, width, height),
                    new Rectangle(x, y, width, height),
                    GraphicsUnit.Pixel);

                using var extractedPhotoStream = new MemoryStream();
                extractedPhoto.Save(extractedPhotoStream, ImageFormat.Png);
                return extractedPhotoStream.ToArray();
            }
            else return null;
        }

        private async Task<ImageAnalysis> AnalyzePhoto(byte[] photoContent)
        {
            using var photoStream = new MemoryStream(photoContent);

            var features = new List<VisualFeatureTypes?>()
            {
                VisualFeatureTypes.Categories, VisualFeatureTypes.Description,
                VisualFeatureTypes.Faces, VisualFeatureTypes.ImageType,
                VisualFeatureTypes.Tags, VisualFeatureTypes.Adult,
                VisualFeatureTypes.Color, VisualFeatureTypes.Brands,
                VisualFeatureTypes.Objects
            };

            return await _computerVisionClient.AnalyzeImageInStreamAsync(
                photoStream,
                features);
        }

        private async Task<string> UploadPhoto(string photo, byte[] photoContent)
        {
            var blobClient = _blobContainerClient.GetBlobClient(photo);
            using var photoStream = new MemoryStream(photoContent);
            await blobClient.UploadAsync(photoStream, true);
            return blobClient.Uri.ToString();
        }

        // DELETE: api/Users/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool UserExists(int id)
        {
            return _context.Users.Any(e => e.Id == id);
        }
    }
}
