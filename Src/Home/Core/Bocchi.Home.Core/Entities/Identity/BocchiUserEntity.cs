using Microsoft.AspNetCore.Identity;

namespace Bocchi.Home.Core.Entities.Identity;

public class BocchiUserEntity : IdentityUser<Guid>
{
    public BocchiUserEntity() : base() { }
    public BocchiUserEntity(string userName) : base(userName) { }
}