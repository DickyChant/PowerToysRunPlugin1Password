// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more info_rmation.

using ManagedCommon;
using Wox.Plugin.Logger;
using Microsoft.PowerToys.Settings.UI.Library;
using OnePassword;
using OnePassword.Accounts;
using OnePassword.Items;
using OnePassword.Vaults;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using Wox.Plugin;
using System.Resources;
using System.Reflection;

namespace Community.PowerToys.Run.Plugin._1Password;

public partial class Main : IPlugin
{

    public static string PluginID => "9576F2CB78674305838C65FD2BE224AC";
    public string? IconPath { get; set; }
    private PluginInitContext? _context;
    public string Name => _rm.GetString("plugin_name");
    public string Description => _rm.GetString("plugin_description");

    
    private OnePasswordManager? _passwordManager;

    private bool _disabled = false;
    private string _disabledReason = string.Empty;

    private List<Item> _items;
    private Dictionary<string, Vault> _vaults;
    private Queue<Vault> _vaultsQueue = new();

    private ResourceManager _rm;
    private PluginSettings _settings;

    private PluginSettings _lastSettings;


    public Main()
    {
        _items = new List<Item>();
        _vaults = new Dictionary<string, Vault>();
        _vaultsQueue = new Queue<Vault>();
        _rm = new ResourceManager("Community.PowerToys.Run.Plugin._1Password.Properties.Resources", Assembly.GetExecutingAssembly());
        _settings = new PluginSettings(_rm);
        _lastSettings = _settings;
    }


    public void Init(PluginInitContext context)
    {

        
        Log.Info("Initializing 1Password plugin", GetType());

        _context = context ?? throw new ArgumentNullException(nameof(context));
        _context.API.ThemeChanged += OnThemeChanged;
        
        UpdateIconPath(_context.API.GetCurrentTheme());


        try
        {
            InitializeConfiguration();
        } catch (Exception ex)
        {
            Log.Error(ex.Message, GetType());
        }

    }

    private void InitializeConfiguration()
    {
        if (!InitializePasswordManager()) return;
        if (!InitializeAccountHandling()) return;
        _passwordManager?.SignIn();
        InitializeLazyVaults();
        InitializeItems();
    }

    private bool InitializePasswordManager()
    {
        Log.Info("Initializing 1Password Manager", GetType());

        if (string.IsNullOrEmpty(_settings.OnePasswordInstallPath))
        {
            DisablePlugin(_rm.GetString("error_missing_required_one_password_cli_path"));
            return false;
        }

        if (_items is null || _vaults is null)
        {
            DisablePlugin(_rm.GetString("error_internal_error_vaults_not_initialized"));
            return false;
        }

        var onePasswordManagerOptions = new OnePasswordManagerOptions { Path = _settings.OnePasswordInstallPath, AppIntegrated = true };
        _passwordManager = new OnePasswordManager(onePasswordManagerOptions);
        return true;
    }

    private bool InitializeAccountHandling()
    {
        Log.Info("Checking for accounts", GetType());

        if (_disabled || _passwordManager is null) return false;

        var accounts = _passwordManager.GetAccounts();
        if (accounts.IsEmpty)
        {
            DisablePlugin(_rm.GetString("error_one_password_no_accounts_found"));
            return false;
        }

        Account? account = null;
        if (accounts.Count > 1)
        {
            if (string.IsNullOrEmpty(_settings.OnePasswordEmail))
            {
                DisablePlugin(_rm.GetString("error_email_not_specified"));
                return false;

            }

            account = accounts.FirstOrDefault(acc => acc.Email == _settings.OnePasswordEmail);
            if (account is null)
            {
                DisablePlugin(_rm.GetString("error_email_found_no_match"));
                return false;

            }
        }

        if (account is null)
        {
            return true;
        }

        _passwordManager.UseAccount(account.Email);

        return true;
    }

    private void InitializeLazyVaults()
    {
        Log.Info("Initializing Lazy Loading", GetType());
;
        if (_disabled || _passwordManager is null) return;


        var allVaults = _passwordManager.GetVaults();
        if (!string.IsNullOrEmpty(_settings.OnePasswordExcludeVault))
        {
            allVaults.RemoveAll(vault => vault.Name == _settings.OnePasswordExcludeVault || vault.Name == _settings.OnePasswordInitVault);
        }

        var initalVault = allVaults.FirstOrDefault(vault => vault.Name == _settings.OnePasswordInitVault);
        if (initalVault is not null)
        {
            _vaults?.Add(initalVault.Id, initalVault);
        }

        foreach (var vault in allVaults)
        {
            _vaultsQueue.Enqueue(vault);
        }
    }

    private void InitializeItems()
    {
        Log.Info("Initializing Items", GetType());

        if (_disabled || _passwordManager is null) return;


        if (_settings.OnePasswordPreloadFavorite) AddItemsFromVault(_passwordManager.SearchForItems(favorite: true));

        foreach (var vault in _vaults.Values)
        {
            AddItemsFromVault(_passwordManager.GetItems(vault));
        }

    }


    private void AddItemsFromVault(IEnumerable<Item> items)
    {
        Log.Info("Adding Items From Vault", GetType());

        if (_disabled || _passwordManager is null) return;

        foreach (var item in items)
        {
            if (!_items?.Any(i => i.Id == item.Id || i?.Vault?.Name == _settings.OnePasswordExcludeVault) ?? false)
            {
                _items?.Add(item);
            }
        }
    }


    private void UpdateIconPath(Theme theme)
    {
        IconPath = theme is Theme.Light or Theme.HighContrastWhite ? "Images/_1PasswordPlugin.light.png" : "Images/_1PasswordPlugin.dark.png";
    }

    private void OnThemeChanged(Theme oldtheme, Theme newTheme)
    {
        UpdateIconPath(newTheme);
    }


    public List<Result> Query(Query query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var results = new List<Result>();
        if (_disabled)
        {
            return [
                new() { 
                    Title = "1password - has been disabled",
                    SubTitle = _disabledReason,
                }
            ];
        }

        return results;
    }

    private void DisablePlugin(string reason)
    {
        _disabled = true;
        _disabledReason = reason;
        Log.Warn(reason, GetType());
    }



}
