using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Southport.UnitTesting.EFCore.SQL.Tests.Database;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> dbContextOptions) : base(dbContextOptions)
    {
    }

    public DbSet<TestEntity> TestEntities { get; set; }


}

public class TestEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [MaxLength(100)] [Required] public string Name { get; set; }
}