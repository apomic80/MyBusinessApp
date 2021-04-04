using Microsoft.EntityFrameworkCore;
using MyBusinessApp.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MyBusinessApp.Server.Data
{
    public class MyBusinessAppDbContext
        : DbContext
    {
        public DbSet<User> Users { get; set; }

        public MyBusinessAppDbContext(DbContextOptions options)
            : base(options)
        {

        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().Ignore(x => x.PhotoContent);
        }
    }
}
