﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrossBot.Core;
using Discord.Commands;
using NHSE.Core;

namespace CrossBot.Discord
{
    // ReSharper disable once UnusedType.Global
    public class DropModule : ModuleBase<SocketCommandContext>
    {
        private static int MaxRequestCount => Globals.Bot.Config.DropConfig.MaxDropCount;

        [Command("clean")]
        [Summary("Picks up items around the bot.")]
        [RequireQueueRole(nameof(Globals.Self.Config.RoleUseBot))]
        public async Task RequestCleanAsync()
        {
            var bot = Globals.Bot;
            if (bot.Config.RequireJoin && bot.Island.GetVisitor(Context.User.Id) == null && !Globals.Self.Config.CanUseSudo(Context.User.Id))
            {
                await ReplyAsync($"You must `{IslandModule.cmdJoin}` the island before using this command.").ConfigureAwait(false);
                return;
            }
            if (!Globals.Bot.Config.AllowClean)
            {
                await ReplyAsync("Clean functionality is currently disabled.").ConfigureAwait(false);
                return;
            }
            Globals.Bot.DropState.CleanRequested = true;
            await ReplyAsync("A clean request will be executed momentarily.").ConfigureAwait(false);
        }

        private const string DropItemSummary =
            "Requests the bot drop an item with the user's provided input. " +
            "Hex Mode: Item IDs (in hex); request multiple by putting spaces between items. " +
            "Text Mode: Item names; request multiple by putting commas between items. To parse for another language, include the language code first and a comma, followed by the items.";

        [Command("dropItem")]
        [Alias("drop")]
        [Summary("Drops a custom item (or items) from an NHI file.")]
        [RequireQueueRole(nameof(Globals.Self.Config.RoleUseBot))]
        public async Task RequestDropAsync()
        {
            var bot = Globals.Bot;
            if (bot.Config.RequireJoin && bot.Island.GetVisitor(Context.User.Id) == null && !Globals.Self.Config.CanUseSudo(Context.User.Id))
            {
                await ReplyAsync($"You must `{IslandModule.cmdJoin}` the island before using this command.").ConfigureAwait(false);
                return;
            }

            if (Context.Message.Attachments.Count == 0)
            {
                await ReplyAsync("No items requested; silly goose. Attach an `nhi` file next time, or request specific items.").ConfigureAwait(false);
                return;
            }

            var att1 = Context.Message.Attachments.ElementAt(0);
            var max = Globals.Bot.Config.DropConfig.MaxDropCount;
            var (code, items) = await DiscordUtil.TryDownloadItems(att1, max).ConfigureAwait(false);
            if (code != DownloadResult.Success)
            {
                var msg = DiscordUtil.GetItemErrorMessage(code, max);
                await ReplyAsync(msg).ConfigureAwait(false);
                return;
            }
            await DropItems(items).ConfigureAwait(false);
        }

        [Command("dropItem")]
        [Alias("drop")]
        [Summary("Drops a custom item (or items).")]
        [RequireQueueRole(nameof(Globals.Self.Config.RoleUseBot))]
        public async Task RequestDropAsync([Summary(DropItemSummary)][Remainder]string request)
        {
            var bot = Globals.Bot;
            if (bot.Config.RequireJoin && bot.Island.GetVisitor(Context.User.Id) == null && !Globals.Self.Config.CanUseSudo(Context.User.Id))
            {
                await ReplyAsync($"You must `{IslandModule.cmdJoin}` the island before using this command.").ConfigureAwait(false);
                return;
            }

            var cfg = bot.Config;
            var items = ItemParser.GetItemsFromUserInput(request, cfg.DropConfig);
            await DropItems(items).ConfigureAwait(false);
        }

        private const string DropDIYSummary =
            "Requests the bot drop a DIY recipe with the user's provided input. " +
            "Hex Mode: DIY Recipe IDs (in hex); request multiple by putting spaces between items. " +
            "Text Mode: DIY Recipe Item names; request multiple by putting commas between items. To parse for another language, include the language code first and a comma, followed by the items.";

        [Command("dropDIY")]
        [Alias("diy")]
        [Summary("Drops a DIY recipe with the requested recipe ID(s).")]
        [RequireQueueRole(nameof(Globals.Self.Config.RoleUseBot))]
        public async Task RequestDropDIYAsync([Summary(DropDIYSummary)][Remainder]string recipeIDs)
        {
            var bot = Globals.Bot;
            if (bot.Config.RequireJoin && bot.Island.GetVisitor(Context.User.Id) == null && !Globals.Self.Config.CanUseSudo(Context.User.Id))
            {
                await ReplyAsync($"You must `{IslandModule.cmdJoin}` the island before using this command.").ConfigureAwait(false);
                return;
            }
            var items = ItemParser.GetDIYsFromUserInput(recipeIDs);
            await DropItems(items).ConfigureAwait(false);
        }

        private async Task DropItems(IReadOnlyCollection<Item> items)
        {
            if (items.Count > MaxRequestCount)
            {
                var clamped = $"Users are limited to {MaxRequestCount} items per command. Please use this bot responsibly.";
                await ReplyAsync(clamped).ConfigureAwait(false);
                items = items.Take(MaxRequestCount).ToArray();
            }

            for (int i = 0; i < items.Count; i++)
            {
                bool canStack = ItemInfo.TryGetMaxStackCount(items.ElementAt(i), out ushort maxStack);
                if (canStack && items.ElementAt(i).Count == 0 && maxStack > 1)
                {
                    items.ElementAt(i).Count = (ushort)(maxStack - 1);
                }
            }

            var user = Context.User;
            var mention = Context.User.Mention;
            var requestInfo = new DropRequest(user.Username, user.Id, items)
            {
                OnFinish = success =>
                {
                    var reply = success
                        ? "Items have been dropped by the bot. Please pick them up!"
                        : "Failed to inject items. Please tell the bot owner to look at the logs!";
                    Task.Run(async () => await ReplyAsync($"{mention}: {reply}").ConfigureAwait(false));
                }
            };
            Globals.Bot.DropState.Injections.Enqueue(requestInfo);

            var msg = $"{mention}: Item drop request{(requestInfo.Items.Count > 1 ? "s" : string.Empty)} have been added to the queue and will be dropped momentarily.";
            await ReplyAsync(msg).ConfigureAwait(false);
        }
    }
}
