using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaissaDesk.Core;

/// <summary>
/// Opens web links in the user's default browser. The Photino web view has no tabs, so JavaScript
/// <c>window.open</c> silently does nothing on some platforms; the OS handler is used instead.
/// </summary>
public interface IExternalLinkService
{
    /// <summary>Opens an absolute http(s) URL. Returns false (and logs why) when it could not be opened.</summary>
    bool TryOpen(string? url);
}

public class ExternalLinkService : IExternalLinkService
{
    private readonly ILogger<ExternalLinkService> _logger;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private readonly Func<bool> _isLinux;

    public ExternalLinkService(ILogger<ExternalLinkService>? logger = null)
        : this(logger, Process.Start, () => RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
    }

    /// <summary>Test seam: process launching and platform detection are injectable.</summary>
    public ExternalLinkService(ILogger<ExternalLinkService>? logger, Func<ProcessStartInfo, Process?> startProcess, Func<bool> isLinux)
    {
        _logger = logger ?? NullLogger<ExternalLinkService>.Instance;
        _startProcess = startProcess;
        _isLinux = isLinux;
    }

    public bool TryOpen(string? url)
    {
        // Only web links: never hand arbitrary paths or custom schemes to the OS shell
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _logger.LogWarning("Refusing to open non-http(s) link {Url}.", url);
            return false;
        }

        string target = uri.AbsoluteUri;
        try
        {
            using var process = _startProcess(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            if (!_isLinux())
            {
                _logger.LogWarning(ex, "Could not open {Url} with the OS shell.", target);
                return false;
            }

            _logger.LogInformation(ex, "Shell open of {Url} failed; falling back to xdg-open.", target);
        }

        try
        {
            var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            psi.ArgumentList.Add(target);
            using var process = _startProcess(psi);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open {Url} with xdg-open.", target);
            return false;
        }
    }
}
