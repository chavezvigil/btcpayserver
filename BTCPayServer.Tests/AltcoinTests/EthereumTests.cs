using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Tests.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using NBitcoin;
using PlaywrightSharp;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    public class EthereumTests
    {
        public const int TestTimeout = 60_000;

        public EthereumTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) {Name = "Tests"};
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact]
        [Trait("Fast", "Fast")]
        [Trait("Altcoins", "Altcoins")]
        public void LoadSubChainsAlways()
        {
            var config = new ConfigurationRoot(new List<IConfigurationProvider>()
            {
                new MemoryConfigurationProvider(new MemoryConfigurationSource()
                {
                    InitialData = new[] {new KeyValuePair<string, string>("chains", "usdt20"),}
                })
            });

            var networkProvider = config.ConfigureNetworkProvider();
            Assert.NotNull(networkProvider.GetNetwork("ETH"));
            Assert.NotNull(networkProvider.GetNetwork("USDT20"));
        }

        [Fact]
        [Trait("Altcoins", "Altcoins")]
        public async Task CanUseEthereum()
        {
            using var s = SeleniumTester.Create("ETHEREUM", true);
            s.Server.ActivateETH();
            await s.StartAsync();
            await s.RegisterNewUser(true);

            IElementHandle syncSummary = null;
            await TestUtils.EventuallyAsync(async () =>
            {
                syncSummary = await s.Driver.QuerySelectorAsync("#modalDialog");
                Assert.NotNull(syncSummary);
                Assert.False(await syncSummary.IsHiddenAsync());
            });
            var web3Link = syncSummary.FindElement(By.LinkText("Configure Web3"));
            web3Link.Click();
            s.Driver.FindElement("#Web3ProviderUrl")).SendKeys("https://ropsten-rpc.linkpool.io");
            s.Driver.FindElement("#saveButton")).Click();
            await s.FindAlertMessage();
            TestUtils.Eventually(() =>
            {
                s.Driver.Navigate().Refresh();
                s.Driver.AssertElementNotFound("#modalDialog"));
            });

            var store = s.CreateNewStore();
            s.Driver.FindElement(By.LinkText("Ethereum")).Click();

            var seed = new Mnemonic(Wordlist.English);
            s.Driver.FindElement("#ModifyETH")).Click();
            s.Driver.FindElement("#Seed")).SendKeys(seed.ToString());
            s.Driver.SetCheckbox("#StoreSeed"), true);
            s.Driver.SetCheckbox("#Enabled"), true);
            s.Driver.FindElement("#SaveButton")).Click();
            await s.FindAlertMessage();
            s.Driver.FindElement("#ModifyUSDT20")).Click();
            s.Driver.FindElement("#Seed")).SendKeys(seed.ToString());
            s.Driver.SetCheckbox("#StoreSeed"), true);
            s.Driver.SetCheckbox("#Enabled"), true);
            s.Driver.FindElement("#SaveButton")).Click();
            await s.FindAlertMessage();

            var invoiceId = s.CreateInvoice(store.storeName, 10);
            s.GoToInvoiceCheckout(invoiceId);
            var currencyDropdownButton = s.Driver.FindElement(By.ClassName("payment__currencies"));
            Assert.Contains("ETH", currencyDropdownButton.Text);
            s.Driver.FindElement("#copy-tab")).Click();

            var ethAddress = s.Driver.FindElements(By.ClassName("copySectionBox"))
                .Single(element => element.FindElement(By.TagName("label")).Text
                    .Contains("Address", StringComparison.InvariantCultureIgnoreCase)).FindElement(By.TagName("input"))
                .GetAttribute("value");
            currencyDropdownButton.Click();
            var elements = s.Driver.FindElement(By.ClassName("vex-content")).FindElements(By.ClassName("vexmenuitem"));
            Assert.Equal(2, elements.Count);

            elements.Single(element => element.Text.Contains("USDT20")).Click();
            s.Driver.FindElement("#copy-tab")).Click();
            var usdtAddress = s.Driver.FindElements(By.ClassName("copySectionBox"))
                .Single(element => element.FindElement(By.TagName("label")).Text
                    .Contains("Address", StringComparison.InvariantCultureIgnoreCase)).FindElement(By.TagName("input"))
                .GetAttribute("value");
            Assert.Equal(usdtAddress, ethAddress);
        }
    }
}
