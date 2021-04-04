using System;
using System.Collections.Generic;
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
            var valid = await CheckPhoto(user.PhotoContent);
            user.Photo = await UploadPhoto(user.Photo, user.PhotoContent);
            _context.Users.Add(user);
            
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetUser", new { id = user.Id }, user);
        }

        private async Task<bool> CheckPhoto(byte[] photoContent)
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

            var result = await _computerVisionClient.AnalyzeImageInStreamAsync(
                photoStream,
                features);

            return result.Faces != null && result.Faces.Count > 0 ;
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
