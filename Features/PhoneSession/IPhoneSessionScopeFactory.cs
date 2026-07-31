using VcfEditor.Core;

namespace VcfEditor.Features.PhoneSession;

public interface IPhoneSessionScopeFactory
{
    PhoneSessionScope Create(PhoneApiClient client);
}
