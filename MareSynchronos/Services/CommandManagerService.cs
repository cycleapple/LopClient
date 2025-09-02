using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI;
using MareSynchronos.WebAPI;
using System.Globalization;
using System.Text;

namespace MareSynchronos.Services;

public sealed class CommandManagerService : IDisposable
{
    private const string _commandName = "/lop";
    private const string _commandName2 = "/loporrit";

    public const string _ssCommandPrefix = "/ss";
    private const string _ssChannelPrefix = "SS";

    public const string _ssCommandPrefix2 = "/lss";
    private const string _ssChannelPrefix2 = "LSS";

    private readonly ApiController _apiController;
    private readonly ICommandManager _commandManager;
    private readonly MareMediator _mediator;
    private readonly MareConfigService _mareConfigService;
    private readonly PerformanceCollectorService _performanceCollectorService;
    private readonly CacheMonitor _cacheMonitor;
    private readonly ChatService _chatService;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    public static bool ChatCommandConflict { get; private set; } = false;
    public static string ChatCommandPrefix { get; private set; } = _ssCommandPrefix;
    public static string ChatChannelPrefix { get; private set; } = _ssChannelPrefix;

    public CommandManagerService(ICommandManager commandManager, PerformanceCollectorService performanceCollectorService,
        ServerConfigurationManager serverConfigurationManager, CacheMonitor periodicFileScanner, ChatService chatService,
        ApiController apiController, MareMediator mediator, MareConfigService mareConfigService)
    {
        _commandManager = commandManager;
        _performanceCollectorService = performanceCollectorService;
        _serverConfigurationManager = serverConfigurationManager;
        _cacheMonitor = periodicFileScanner;
        _chatService = chatService;
        _apiController = apiController;
        _mediator = mediator;
        _mareConfigService = mareConfigService;
        _commandManager.AddHandler(_commandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Loporrit UI"
        });
        _commandManager.AddHandler(_commandName2, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Loporrit UI"
        });

        var cmd = new CommandInfo(OnChatCommand)
        {
            ShowInHelp = false
        };

        // Lazy registration of all possible /ss# commands which tbf is what the game does for linkshells anyway
        if (_commandManager.AddHandler($"{_ssCommandPrefix}1", cmd))
        {
            for (int i = 2; i <= ChatService.CommandMaxNumber; ++i)
                _commandManager.AddHandler($"{_ssCommandPrefix}{i}", cmd);
        }
        else
        {
            ChatCommandConflict = true;
            ChatCommandPrefix = _ssCommandPrefix2;
            ChatChannelPrefix = _ssChannelPrefix2;
        }

        for (int i = 1; i <= ChatService.CommandMaxNumber; ++i)
            _commandManager.AddHandler($"{_ssCommandPrefix2}{i}", cmd);
    }

    public void Dispose()
    {
        _commandManager.RemoveHandler(_commandName);
        _commandManager.RemoveHandler(_commandName2);

        if (!ChatCommandConflict)
        {
            for (int i = 1; i <= ChatService.CommandMaxNumber; ++i)
                _commandManager.RemoveHandler($"{_ssCommandPrefix}{i}");
        }

        for (int i = 1; i <= ChatService.CommandMaxNumber; ++i)
            _commandManager.RemoveHandler($"{_ssCommandPrefix2}{i}");
    }

    private void OnCommand(string command, string args)
    {
        var splitArgs = args.ToLowerInvariant().Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries);

        if (splitArgs.Length == 0)
        {
            // Interpret this as toggling the UI
            if (_mareConfigService.Current.HasValidSetup())
                _mediator.Publish(new UiToggleMessage(typeof(CompactUi)));
            else
                _mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
            return;
        }

        if (string.Equals(splitArgs[0], "toggle", StringComparison.OrdinalIgnoreCase))
        {
            if (_apiController.ServerState == WebAPI.SignalR.Utils.ServerState.Disconnecting)
            {
                _mediator.Publish(new NotificationMessage("Loporrit disconnecting", "Cannot use /toggle while Loporrit is still disconnecting",
                    NotificationType.Error));
            }

            if (_serverConfigurationManager.CurrentServer == null) return;
            var fullPause = splitArgs.Length > 1 ? splitArgs[1] switch
            {
                "on" => false,
                "off" => true,
                _ => !_serverConfigurationManager.CurrentServer.FullPause,
            } : !_serverConfigurationManager.CurrentServer.FullPause;

            if (fullPause != _serverConfigurationManager.CurrentServer.FullPause)
            {
                _serverConfigurationManager.CurrentServer.FullPause = fullPause;
                _serverConfigurationManager.Save();
                _ = _apiController.CreateConnections();
            }
        }
        else if (string.Equals(splitArgs[0], "gpose", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(CharaDataHubUi)));
        }
        else if (string.Equals(splitArgs[0], "rescan", StringComparison.OrdinalIgnoreCase))
        {
            _cacheMonitor.InvokeScan();
        }
        else if (string.Equals(splitArgs[0], "perf", StringComparison.OrdinalIgnoreCase))
        {
            if (splitArgs.Length > 1 && int.TryParse(splitArgs[1], CultureInfo.InvariantCulture, out var limitBySeconds))
            {
                _performanceCollectorService.PrintPerformanceStats(limitBySeconds);
            }
            else
            {
                _performanceCollectorService.PrintPerformanceStats();
            }
        }
        else if (string.Equals(splitArgs[0], "medi", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.PrintSubscriberInfo();
        }
        else if (string.Equals(splitArgs[0], "analyze", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(DataAnalysisUi)));
        }
        else if (string.Equals(splitArgs[0], "vanity", StringComparison.OrdinalIgnoreCase))
        {
            if (!_apiController.IsConnected)
            {
                _mediator.Publish(new NotificationMessage("Loporrit disconnected", "Cannot use /vanity while disconnected", NotificationType.Error));
                return;
            }

            // Re-split but preserve case
            splitArgs = args.Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries);
            var newUid = splitArgs.Length >= 2 ? splitArgs[1].Trim() : string.Empty;

            _ = Task.Run(async () =>
            {
                try
                {
                    bool result = await _apiController.UserUpdateAlias(newUid).ConfigureAwait(false);
                    if (result)
                        _mediator.Publish(new NotificationMessage("Success!", "Your vanity UID was accepted", NotificationType.Info));
                    else
                        _mediator.Publish(new NotificationMessage("Failure!", "Your vanity UID was rejected", NotificationType.Info));
                }
                catch
                {
                    _mediator.Publish(new NotificationMessage("Failure!", "Your vanity UID caused an error!", NotificationType.Info));
                }
            });
        }
    }

    private void OnChatCommand(string command, string args)
    {
        if (_mareConfigService.Current.DisableSyncshellChat)
            return;

        int shellNumber = int.Parse(command[_ssCommandPrefix.Length..]);

        if (args.Length == 0)
        {
            _chatService.SwitchChatShell(shellNumber);
        }
        else
        {
            // FIXME: Chat content seems to already be stripped of any special characters here?
            byte[] chatBytes = Encoding.UTF8.GetBytes(args);
            _chatService.SendChatShell(shellNumber, chatBytes);
        }
    }
}