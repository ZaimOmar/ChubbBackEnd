using Chubb.Identity.Domain;

namespace Chubb.Identity.Application;

public interface ITokenIssuer
{
    AccessToken Issue(User user);
}
