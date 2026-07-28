namespace CreatorControlSuite.Modules.OBS.Protocol;

internal static class ObsHandshake
{
    internal const int SupportedRpcVersion = 1;
    internal const int DefaultEventSubscriptions = 66031;

    internal static ObsEnvelope CreateIdentify(
        ObsHello hello,
        string? password)
    {
        ArgumentNullException.ThrowIfNull(hello);

        if (hello.RpcVersion < SupportedRpcVersion)
        {
            throw new InvalidOperationException(
                $"OBS RPC-Version {hello.RpcVersion} wird nicht unterstützt.");
        }

        string? authentication = null;

        if (hello.Authentication is not null)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    "OBS verlangt ein WebSocket-Passwort.");
            }

            if (string.IsNullOrWhiteSpace(hello.Authentication.Salt) ||
                string.IsNullOrWhiteSpace(hello.Authentication.Challenge))
            {
                throw new InvalidOperationException(
                    "OBS sendete eine ungültige Authentifizierungsanforderung.");
            }

            authentication = ObsAuthentication.CreateResponse(
                password,
                hello.Authentication.Salt,
                hello.Authentication.Challenge);
        }

        return new ObsEnvelope
        {
            Op = 1,
            Data = new ObsIdentify
            {
                RpcVersion = SupportedRpcVersion,
                Authentication = authentication,
                EventSubscriptions = DefaultEventSubscriptions
            }
        };
    }
}
