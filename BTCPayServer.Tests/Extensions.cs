using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using PlaywrightSharp;
using Xunit;

namespace BTCPayServer.Tests
{
    public static class Extensions
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        public static string ToJson(this object o) => JsonConvert.SerializeObject(o, Formatting.None, JsonSettings);

        public static async Task LogIn(this SeleniumTester s, string email)
        {
            await s.Driver.TypeAsync("#Email", email);
            await s.Driver.TypeAsync("#Password", "123456");
            await s.Driver.ClickAsync("#LoginButton");
            await s.AssertNoError();
        }

        public static async Task AssertNoError(this SeleniumTester tester)
        {
            try
            {
                Assert.NotNull(await tester.Driver.QuerySelectorAsync(".navbar-brand"));
                var dangerAlerts = await tester.Driver.QuerySelectorAllAsync(".alert-danger");
                if (dangerAlerts.Any())
                {
                    foreach (var dangerAlert in dangerAlerts)
                        Assert.False(await dangerAlert.IsVisibleAsync(), "No alert should be displayed");
                }
            }
            catch
            {
                StringBuilder builder = new StringBuilder();
                builder.AppendLine();
                foreach (var logKind in new[] { LogType.Browser, LogType.Client, LogType.Driver, LogType.Server })
                {
                    try
                    {
                        tester.Browser.Contexts.First().
                        var logs = tester.Manage().Logs.GetLog(logKind);
                        builder.AppendLine($"Selenium [{logKind}]:");
                        foreach (var entry in logs)
                        {
                            builder.AppendLine($"[{entry.Level}]: {entry.Message}");
                        }
                    }
                    catch
                    {
                        // ignored
                    }

                    builder.AppendLine("---------");
                }
                Logs.Tester.LogInformation(builder.ToString());
                builder = new StringBuilder();
                builder.AppendLine("Selenium [Sources]:");
                builder.AppendLine(tester.Browser.);
                builder.AppendLine("---------");
                Logs.Tester.LogInformation(builder.ToString());
                throw;
            }
        }
        public static T AssertViewModel<T>(this IActionResult result)
        {
            Assert.NotNull(result);
            var vr = Assert.IsType<ViewResult>(result);
            return Assert.IsType<T>(vr.Model);
        }
        public static async Task<T> AssertViewModelAsync<T>(this Task<IActionResult> task)
        {
            var result = await task;
            Assert.NotNull(result);
            var vr = Assert.IsType<ViewResult>(result);
            return Assert.IsType<T>(vr.Model);
        }

        public static void AssertElementNotFound(this IWebDriver driver, By by)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            var wait = SeleniumTester.ImplicitWait;

            while (DateTimeOffset.UtcNow - now < wait)
            {
                try
                {
                    var webElement = driver.FindElement(by);
                    if (!webElement.Displayed)
                        return;
                }
                catch (NoSuchWindowException)
                {
                    return;
                }
                catch (NoSuchElementException)
                {
                    return;
                }
                Thread.Sleep(50);
            }
            Assert.False(true, "Elements was found");
        }

        public static void UntilJsIsReady(this WebDriverWait wait)
        {
            wait.Until(d=>((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));
            wait.Until(d=>((IJavaScriptExecutor)d).ExecuteScript("return typeof(jQuery) === 'undefined' || jQuery.active === 0").Equals(true));
        }

        public static IElementHandle WaitForElement(this IWebDriver driver, By selector)
        {
            var wait = new WebDriverWait(driver, SeleniumTester.ImplicitWait);
            wait.UntilJsIsReady();

            var el = driver.FindElement(selector);
            wait.Until(d => el.Displayed);

            return el;
        }

        public static void WaitForAndClick(this IWebDriver driver, By selector)
        {
            var wait = new WebDriverWait(driver, SeleniumTester.ImplicitWait);
            wait.UntilJsIsReady();

            var el = driver.FindElement(selector);
            wait.Until(d => el.Displayed && el.Enabled);
            el.Click();

            wait.UntilJsIsReady();
        }

        public static void SetCheckbox(this IWebDriver driver, By selector, bool value)
        {
            var element = driver.FindElement(selector);
            if ((value && !element.Selected) || (!value && element.Selected))
            {
                driver.WaitForAndClick(selector);
            }

            if (value != element.Selected)
            {
                Logs.Tester.LogInformation("SetCheckbox recursion, trying to click again");
                driver.SetCheckbox(selector, value);
            }
        }
    }
}
