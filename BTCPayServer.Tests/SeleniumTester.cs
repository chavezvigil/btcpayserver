using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Tests.Logging;
using BTCPayServer.Views.Manage;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Stores;
using BTCPayServer.Views.Wallets;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using BTCPayServer.BIP78.Sender;
using PlaywrightSharp;
using PlaywrightSharp.Chromium;
using Xunit;

namespace BTCPayServer.Tests
{
    public class SeleniumTester : IDisposable
    {
        public IPage Driver { get; set; }
        public ServerTester Server { get; set; }
        public WalletId WalletId { get; set; }

        public string StoreId { get; set; }

        public static SeleniumTester Create([CallerMemberNameAttribute] string scope = null, bool newDb = false) =>
            new SeleniumTester { Server = ServerTester.Create(scope, newDb) };

        public static readonly TimeSpan ImplicitWait = TimeSpan.FromSeconds(5);

        public async Task StartAsync()
        {
            await Server.StartAsync();

            var windowSize = (Width: 1200, Height: 1000);
            var builder = new ConfigurationBuilder();
            builder.AddUserSecrets("AB0AC1DD-9D26-485B-9416-56A33F268117");
            var config = builder.Build();
            this.PlayWright = await Playwright.CreateAsync();
            // Run `dotnet user-secrets set RunSeleniumInBrowser true` to run tests in browser
            var runInBrowser = config["RunSeleniumInBrowser"] == "true";
            // Reset this using `dotnet user-secrets remove RunSeleniumInBrowser`

            // var chromeDriverPath = config["ChromeDriverDirectory"] ?? (Server.PayTester.InContainer ? "/usr/bin" : Directory.GetCurrentDirectory());

            var options = new List<string>();
           
            // options.Add($"window-size={windowSize.Width}x{windowSize.Height}");
            // options.Add("shm-size=2g");


            this.Browser = await this.PlayWright.Chromium.LaunchAsync(
                headless:!runInBrowser, 
                chromiumSandbox:!Server.PayTester.InContainer,
                args:options.ToArray()
                );

            this.Context = await Browser.NewContextAsync(new ViewportSize()
            {
                Width = windowSize.Width, Height = windowSize.Height
            });
            
            Driver = await Context.NewPageAsync();
            // if (runInBrowser)
            // {
            //     // ensure maximized window size
            //     // otherwise TESTS WILL FAIL because of different hierarchy in navigation menu
            //     Browser..Window.Maximize();
            // }

            Logs.Tester.LogInformation($"Selenium: Using {Driver.GetType()}");
            Logs.Tester.LogInformation($"Selenium: Browsing to {Server.PayTester.ServerUri}");
            Logs.Tester.LogInformation($"Selenium: Resolution {Driver.ViewportSize}");
            await GoToRegister();
            Driver.AssertNoError();
        }

        public IChromiumBrowserContext Context { get; set; }

        public IChromiumBrowser Browser { get; set; }

        public IPlaywright PlayWright { get; internal set; }

        internal async Task<IElementHandle> FindAlertMessage(StatusMessageModel.StatusSeverity severity = StatusMessageModel.StatusSeverity.Success)
        {
            var className = $".alert-{StatusMessageModel.ToString(severity)}";
            var el = await Driver.QuerySelectorAsync(className) ?? await Driver.WaitForSelectorAsync(className);
            if (el is null)
                throw new NoSuchElementException($"Unable to find {className}");
            return el;
        }

        public string Link(string relativeLink)
        {
            return Server.PayTester.ServerUri.AbsoluteUri.WithoutEndingSlash() + relativeLink.WithStartingSlash();
        }

        public async Task GoToRegister()
        {
            await Driver.GoToAsync(Link("/register"));
        }

        public async Task<string> RegisterNewUser(bool isAdmin = false)
        {
            var usr = RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
            Logs.Tester.LogInformation($"User: {usr} with password 123456");
            await Driver.TypeAsync("#Email", usr);
           await Driver.TypeAsync("#Password","123456");
           await Driver.TypeAsync("#ConfirmPassword", "123456");
           if (isAdmin)
               await Driver.CheckAsync("#IsAdmin");
            await Driver.ClickAsync("#RegisterButton");
            Driver.AssertNoError();
            return usr;
        }

        public (string storeName, string storeId) CreateNewStore()
        {
            Driver.WaitForElement("#Stores")).Click();
            Driver.WaitForElement("#CreateStore")).Click();
            var name = "Store" + RandomUtils.GetUInt64();
            Driver.WaitForElement("#Name")).SendKeys(name);
            Driver.WaitForElement("#Create")).Click();
            StoreId = Driver.WaitForElement("#Id")).GetAttribute("value");
            return (name, StoreId);
        }

        public Mnemonic GenerateWallet(string cryptoCode = "BTC", string seed = "", bool importkeys = false, bool privkeys = false, ScriptPubKeyType format = ScriptPubKeyType.Segwit)
        {
            Driver.FindElement(By.Id($"Modify{cryptoCode}")).Click();

            // Replace previous wallet case
            if (Driver.PageSource.Contains("id=\"ChangeWalletLink\""))
            {
                Driver.FindElement("#ChangeWalletLink")).Click();
                Driver.FindElement("#continue")).Click();
            }

            if (string.IsNullOrEmpty(seed))
            {
                var option = privkeys ? "Hotwallet" : "Watchonly";
                Logs.Tester.LogInformation($"Generating new seed ({option})");
                Driver.FindElement("#GenerateWalletLink")).Click();
                Driver.FindElement(By.Id($"Generate{option}Link")).Click();
            }
            else
            {
                Logs.Tester.LogInformation("Progressing with existing seed");
                Driver.FindElement("#ImportWalletOptionsLink")).Click();
                Driver.FindElement("#ImportSeedLink")).Click();
                Driver.FindElement("#ExistingMnemonic")).SendKeys(seed);
                Driver.SetCheckbox("#SavePrivateKeys"), privkeys);
            }

            Driver.FindElement("#ScriptPubKeyType")).Click();
            Driver.FindElement(By.CssSelector($"#ScriptPubKeyType option[value={format}]")).Click();

            // Open advanced settings via JS, because if we click the link it triggers the toggle animation.
            // This leads to Selenium trying to click the button while it is moving resulting in an error.
            Driver.ExecuteJavaScript("document.getElementById('AdvancedSettings').classList.add('show')");

            Driver.SetCheckbox("#ImportKeysToRPC"), importkeys);
            Driver.FindElement("#Continue")).Click();

            // Seed backup page
            FindAlertMessage();
            if (string.IsNullOrEmpty(seed))
            {
                seed = Driver.FindElements("#RecoveryPhrase")).First().GetAttribute("data-mnemonic");
            }

            // Confirm seed backup
            Driver.FindElement("#confirm")).Click();
            Driver.FindElement("#submit")).Click();

            WalletId = new WalletId(StoreId, cryptoCode);
            return new Mnemonic(seed);
        }

        public void AddDerivationScheme(string cryptoCode = "BTC", string derivationScheme = "xpub661MyMwAqRbcGABgHMUXDzPzH1tU7eZaAaJQXhDXsSxsqyQzQeU6kznNfSuAyqAK9UaWSaZaMFdNiY5BCF4zBPAzSnwfUAwUhwttuAKwfRX-[legacy]")
        {
            Driver.FindElement(By.Id($"Modify{cryptoCode}")).Click();
            Driver.FindElement("#ImportWalletOptionsLink")).Click();
            Driver.FindElement("#ImportXpubLink")).Click();
            Driver.FindElement("#DerivationScheme")).SendKeys(derivationScheme);
            Driver.FindElement("#Continue")).Click();
            Driver.FindElement("#Confirm")).Click();
            FindAlertMessage();
        }

        public void AddLightningNode(string cryptoCode = "BTC", LightningConnectionType? connectionType = null)
        {
            Driver.FindElement(By.Id($"Modify-Lightning{cryptoCode}")).Click();

            var connectionString = connectionType switch
            {
                LightningConnectionType.Charge =>
                    $"type=charge;server={Server.MerchantCharge.Client.Uri.AbsoluteUri};allowinsecure=true",
                LightningConnectionType.CLightning =>
                    $"type=clightning;server={((CLightningClient) Server.MerchantLightningD).Address.AbsoluteUri}",
                LightningConnectionType.LndREST =>
                    $"type=lnd-rest;server={Server.MerchantLnd.Swagger.BaseUrl};allowinsecure=true",
                _ => null
            };

            if (connectionString == null)
            {
                Assert.True(Driver.FindElement("#LightningNodeType-Internal")).Enabled, "Usage of the internal Lightning node is disabled.");
                Driver.FindElement(By.CssSelector("label[for=\"LightningNodeType-Internal\"]")).Click();
            }
            else
            {
                Driver.FindElement(By.CssSelector("label[for=\"LightningNodeType-Custom\"]")).Click();
                Driver.FindElement("#ConnectionString")).SendKeys(connectionString);

                Driver.FindElement("#test")).Click();
                Assert.Contains("Connection to the Lightning node successful.", FindAlertMessage().Text);
            }

            Driver.FindElement("#save")).Click();
            Assert.Contains($"{cryptoCode} Lightning node updated.", FindAlertMessage().Text);

            var enabled = Driver.FindElement(By.Id($"{cryptoCode}LightningEnabled"));
            if (enabled.Text == "Enable")
            {
                enabled.Click();
                Assert.Contains($"{cryptoCode} Lightning payments are now enabled for this store.", FindAlertMessage().Text);
            }
        }

        public void ClickOnAllSideMenus()
        {
            var links = Driver.FindElements(By.CssSelector(".nav .nav-link")).Select(c => c.GetAttribute("href")).ToList();
            Driver.AssertNoError();
            Assert.NotEmpty(links);
            foreach (var l in links)
            {
                Logs.Tester.LogInformation($"Checking no error on {l}");
                Driver.Navigate().GoToUrl(l);
                Driver.AssertNoError();
            }
        }

        public void Dispose()
        {
            if (Driver != null)
            {
                try
                {
                    Driver.Quit();
                }
                catch
                {
                    // ignored
                }

                Driver.Dispose();
            }

            Server?.Dispose();
        }

        internal void AssertNotFound()
        {
            Assert.Contains("404 - Page not found</h1>", Driver.PageSource);
        }

        public void GoToHome()
        {
            Driver.Navigate().GoToUrl(Server.PayTester.ServerUri);
        }

        public void Logout()
        {
            Driver.FindElement("#Logout")).Click();
        }

        public void Login(string user, string password)
        {
            Driver.FindElement("#Email")).SendKeys(user);
            Driver.FindElement("#Password")).SendKeys(password);
            Driver.FindElement("#LoginButton")).Click();
        }

        public void GoToStores()
        {
            Driver.FindElement("#Stores")).Click();
        }

        public void GoToStore(string storeId, StoreNavPages storeNavPage = StoreNavPages.Index)
        {
            Driver.FindElement("#Stores")).Click();
            Driver.FindElement(By.Id($"update-store-{storeId}")).Click();

            if (storeNavPage != StoreNavPages.Index)
            {
                Driver.FindElement(By.Id(storeNavPage.ToString())).Click();
            }
        }

        public void GoToInvoiceCheckout(string invoiceId)
        {
            Driver.FindElement("#Invoices")).Click();
            Driver.FindElement(By.Id($"invoice-checkout-{invoiceId}")).Click();
            CheckForJSErrors();
        }

        public void GoToInvoices()
        {
            Driver.FindElement("#Invoices")).Click();
        }

        public void GoToProfile(ManageNavPages navPages = ManageNavPages.Index)
        {
            Driver.FindElement("#MySettings")).Click();
            if (navPages != ManageNavPages.Index)
            {
                Driver.FindElement(By.Id(navPages.ToString())).Click();
            }
        }

        public void GoToLogin()
        {
            Driver.Navigate().GoToUrl(new Uri(Server.PayTester.ServerUri, "/login"));
        }

        public string CreateInvoice(string storeName, decimal amount = 100, string currency = "USD", string refundEmail = "")
        {
            GoToInvoices();
            Driver.FindElement("#CreateNewInvoice")).Click();
            Driver.FindElement("#Amount")).SendKeys(amount.ToString(CultureInfo.InvariantCulture));
            var currencyEl = Driver.FindElement("#Currency"));
            currencyEl.Clear();
            currencyEl.SendKeys(currency);
            Driver.FindElement("#BuyerEmail")).SendKeys(refundEmail);
            Driver.FindElement(By.Name("StoreId")).SendKeys(storeName);
            Driver.FindElement("#Create")).Click();

            var statusElement = FindAlertMessage();
            var id = statusElement.Text.Split(" ")[1];
            return id;
        }

        public async Task FundStoreWallet(WalletId walletId = null, int coins = 1, decimal denomination = 1m)
        {
            walletId ??= WalletId;
            GoToWallet(walletId, WalletsNavPages.Receive);
            Driver.FindElement("#generateButton")).Click();
            var addressStr = Driver.FindElement("#address")).GetProperty("value");
            var address = BitcoinAddress.Create(addressStr, ((BTCPayNetwork)Server.NetworkProvider.GetNetwork(walletId.CryptoCode)).NBitcoinNetwork);
            for (var i = 0; i < coins; i++)
            {
                await Server.ExplorerNode.SendToAddressAsync(address, Money.Coins(denomination));
            }
        }

        public void PayInvoice(WalletId walletId, string invoiceId)
        {
            GoToInvoiceCheckout(invoiceId);
            var bip21 = Driver.FindElement(By.ClassName("payment__details__instruction__open-wallet__btn"))
                .GetAttribute("href");
            Assert.Contains($"{PayjoinClient.BIP21EndpointKey}", bip21);

            GoToWallet(walletId);
            Driver.FindElement("#bip21parse")).Click();
            Driver.SwitchTo().Alert().SendKeys(bip21);
            Driver.SwitchTo().Alert().Accept();
            Driver.FindElement("#SendMenu")).Click();
            Driver.FindElement(By.CssSelector("button[value=nbx-seed]")).Click();
            Driver.FindElement(By.CssSelector("button[value=broadcast]")).Click();
        }

        private void CheckForJSErrors()
        {
            //wait for seleniun update: https://stackoverflow.com/questions/57520296/selenium-webdriver-3-141-0-driver-manage-logs-availablelogtypes-throwing-syste
            //            var errorStrings = new List<string>
            //            {
            //                "SyntaxError",
            //                "EvalError",
            //                "ReferenceError",
            //                "RangeError",
            //                "TypeError",
            //                "URIError"
            //            };
            //
            //            var jsErrors = Driver.Manage().Logs.GetLog(LogType.Browser).Where(x => errorStrings.Any(e => x.Message.Contains(e)));
            //
            //            if (jsErrors.Any())
            //            {
            //                Logs.Tester.LogInformation("JavaScript error(s):" + Environment.NewLine + jsErrors.Aggregate("", (s, entry) => s + entry.Message + Environment.NewLine));
            //            }
            //            Assert.Empty(jsErrors);

        }

        public void GoToWallet(WalletId walletId = null, WalletsNavPages navPages = WalletsNavPages.Send)
        {
            walletId ??= WalletId;
            Driver.Navigate().GoToUrl(new Uri(Server.PayTester.ServerUri, $"wallets/{walletId}"));
            if (navPages != WalletsNavPages.Transactions)
            {
                Driver.FindElement(By.Id($"Wallet{navPages}")).Click();
            }
        }

        public void GoToUrl(string relativeUrl)
        {
            Driver.Navigate().GoToUrl(new Uri(Server.PayTester.ServerUri, relativeUrl));
        }

        public void GoToServer(ServerNavPages navPages = ServerNavPages.Index)
        {
            Driver.FindElement("#ServerSettings")).Click();
            if (navPages != ServerNavPages.Index)
            {
                Driver.FindElement(By.Id($"Server-{navPages}")).Click();
            }
        }

        public void GoToInvoice(string id)
        {
            GoToInvoices();
            foreach (var el in Driver.FindElements(By.ClassName("invoice-details-link")))
            {
                if (el.GetAttribute("href").Contains(id, StringComparison.OrdinalIgnoreCase))
                {
                    el.Click();
                    break;
                }
            }
        }
    }

    public class NoSuchElementException : Exception
    {
        public NoSuchElementException(string message):base(message)
        {
            
        }
    }
}
