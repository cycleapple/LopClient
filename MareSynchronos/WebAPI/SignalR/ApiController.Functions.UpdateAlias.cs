using MareSynchronos.API.Dto.Group;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    public async Task<bool> UserUpdateAlias(string newAlias)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<bool>(nameof(UserUpdateAlias), newAlias).ConfigureAwait(false);
    }

    public async Task<bool> GroupUpdateAlias(GroupDto group, string newAlias)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<bool>(nameof(GroupUpdateAlias), group, newAlias).ConfigureAwait(false);
    }
}
