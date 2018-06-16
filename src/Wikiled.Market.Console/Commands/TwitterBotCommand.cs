﻿using System;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using Trady.Importer;
using Tweetinvi;
using Tweetinvi.Models;
using Tweetinvi.Parameters;
using Wikiled.Console.Arguments;
using Wikiled.Market.Analysis;
using Wikiled.Twitter.Security;

namespace Wikiled.Market.Console.Commands
{
    /// <summary>
    /// Wikiled.Market.Console.exe bot -Stocks=AMD,GOOG,FB,MMM,CAT,AMZN,AXP,KO,INTC,PM,TIF,WFC,MS,JPM
    /// </summary>
    public class TwitterBotCommand : Command
    {
        private static Logger log = LogManager.GetCurrentClassLogger();

        private ITwitterCredentials cred;

        public override string Name => "Bot";

        [Description("For what stocks generate prediction")]
        public string Stocks { get; set; }

        public override Task Execute()
        {
            log.Info("Loading security...");
            RateLimit.RateLimitTrackerMode = RateLimitTrackerMode.TrackAndAwait;
            var auth = new PersistedAuthentication(new PinConsoleAuthentication());
            cred = auth.Authenticate(Analysis.Credentials.TwitterCredentials);
            Process();
            return Task.CompletedTask;
        }

        private void Process()
        {
            var instance = new AnalysisManager(new DataSource(new QuandlWikiImporter(Analysis.Credentials.QuandlKey)), new ClassifierFactory());
            using (Observable.Interval(TimeSpan.FromHours(12)).StartWith(0).Subscribe(item => ProcessMarket(instance)))
            {
                log.Info("Press enter to stop");
                System.Console.ReadLine();
            }
        }

        private void ProcessMarket(AnalysisManager instance)
        {
            var stocks = Stocks.Split(',');
            log.Info("Processing market");
            foreach (var stock in stocks)
            {
                log.Info("Processing {0}", stock);
                StringBuilder text = new StringBuilder();
                var result = instance.Start(stock).Result;
                var sellAccuracy = result.Performance.PerClassMatrices[0].Accuracy;
                var buyAccuracy = result.Performance.PerClassMatrices[1].Accuracy;
                text.AppendLine($"${stock} trading signals ({sellAccuracy * 100:F0}%/{buyAccuracy * 100:F0}%)");
                for (int i = 0; i < result.Predictions.Length; i++)
                {
                    var prediction = result.Predictions[result.Predictions.Length - i - 1];
                    log.Info("{2}, Predicted T-{0}: {1}\r\n", i, prediction, stock);
                    text.AppendFormat("T-{0}: {1}\r\n", i, prediction);
                }

                PublishMessage(text.ToString());
            }
        }

        private void PublishMessage(string text)
        {
            Auth.ExecuteOperationWithCredentials(
                cred,
                () =>
                    {
                        var message = Tweet.PublishTweet(text, new PublishTweetOptionalParameters { });
                        if (message == null)
                        {
                            var exception = ExceptionHandler.GetLastException();
                            if (exception != null)
                            {
                                foreach (var exceptionTwitterExceptionInfo in exception.TwitterExceptionInfos)
                                {
                                    log.Error(exceptionTwitterExceptionInfo.Message);
                                }
                            }
                        }
                    });
        }
    }
}