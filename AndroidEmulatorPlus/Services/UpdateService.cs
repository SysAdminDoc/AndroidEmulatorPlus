using Velopack;
using Velopack.Sources;

namespace AndroidEmulatorPlus.Services;

public sealed record UpdateCheckResult(bool UpdateFound, bool Downloaded, bool Restarting, string Message);

public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/SysAdminDoc/AndroidEmulatorPlus";

    private readonly LogService _log;

    public UpdateService(LogService log)
    {
        _log = log;
    }

    public async Task<UpdateCheckResult> CheckAndDownloadAsync(bool restart)
    {
        try
        {
            var manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
            _log.Info("Checking GitHub Releases for application updates...");

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                const string message = "AndroidEmulatorPlus is up to date.";
                _log.Success(message);
                return new UpdateCheckResult(UpdateFound: false, Downloaded: false, Restarting: false, message);
            }

            _log.Info("Update found. Downloading Velopack package...");
            await manager.DownloadUpdatesAsync(update);

            if (restart)
            {
                _log.Info("Applying update and restarting...");
                manager.ApplyUpdatesAndRestart(update);
                return new UpdateCheckResult(UpdateFound: true, Downloaded: true, Restarting: true,
                    "Update downloaded. Restarting to apply it...");
            }

            const string pendingMessage = "Update downloaded. It will be applied on the next app launch.";
            _log.Success(pendingMessage);
            return new UpdateCheckResult(UpdateFound: true, Downloaded: true, Restarting: false, pendingMessage);
        }
        catch (Exception ex) when (ex.GetType().Name == "NotInstalledException")
        {
            const string message = "Update checks require a Velopack-installed copy.";
            _log.Detail(message);
            return new UpdateCheckResult(UpdateFound: false, Downloaded: false, Restarting: false, message);
        }
        catch (Exception ex) when (ex.GetType().Name == "AcquireLockFailedException")
        {
            const string message = "Another update operation is already running.";
            _log.Warning(message);
            return new UpdateCheckResult(UpdateFound: false, Downloaded: false, Restarting: false, message);
        }
        catch (Exception ex)
        {
            var message = "Update check failed: " + ex.Message;
            _log.Warning(message);
            return new UpdateCheckResult(UpdateFound: false, Downloaded: false, Restarting: false, message);
        }
    }
}
