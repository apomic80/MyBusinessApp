using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyBusinessApp.Shared
{
    public class User
    {
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        public string FirstName { get; set; }

        [Required]
        [StringLength(200)] 
        public string LastName { get; set; }

        [StringLength(255)]
        public string Photo { get; set; }

        public byte[] PhotoContent { get; set; }

        [StringLength(500)]
        public string Description { get; set; }
    }
}
