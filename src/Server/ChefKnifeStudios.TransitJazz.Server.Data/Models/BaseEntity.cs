using ChefKnifeStudios.TransitJazz.Server.Data.Repos;
using System.ComponentModel.DataAnnotations;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Models;

public abstract class BaseEntity : IAggregateRoot
{
    [Key]
    public virtual int Id { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? ModifiedOnUtc { get; set; }
}
