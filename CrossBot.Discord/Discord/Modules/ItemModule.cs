using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using NHSE.Core;

namespace CrossBot.Discord
{
    // ReSharper disable once UnusedType.Global
    public class ItemModule : ModuleBase<SocketCommandContext>
    {
        [Command("lookupLang")]
        [Alias("ll")]
        [Summary("Gets a list of items that contain the request string.")]
        [RequireQueueRole(nameof(Globals.Self.Config.RoleUseBot))]
        public async Task SearchItemsAsync([Summary("Language code to search with")] string language, [Summary("Item name / item substring")][Remainder] string itemName)
        {
            var strings = GameInfo.GetStrings(language).ItemDataSource;
            await PrintItemsAsync(itemName, strings).ConfigureAwait(false);
        }

        [Command("lookup")]
        [Alias("li", "search")]
        [Summary("Gets a list of items that contain the request string.")]
        [RequireQueueRole(nameof(Globals.Self.Config.RoleUseBot))]
        public async Task SearchItemsAsync([Summary("Item name / item substring")][Remainder] string itemName)
        {
            var strings = GameInfo.Strings.ItemDataSource;
            await PrintItemsAsync(itemName, strings).ConfigureAwait(false);
        }

        [Command("lookupPage")]
        [Alias("lp", "searchPage")]
        [Summary("Gets a list of items with page that contain the request string.")]
        [RequireQueueRole(nameof(Globals.Self.Config.RoleUseBot))]
        public async Task SearchItemsAsync([Summary("Page number")] int page, [Summary("Item name / item substring")][Remainder] string itemName)
        {
            var strings = GameInfo.Strings.ItemDataSource;
            await PrintItemsAsync(itemName, strings, page).ConfigureAwait(false);
        }

        private async Task PrintItemsAsync(string itemName, IReadOnlyList<ComboItem> strings, int page = 1)
        {
            const int minLength = 0;
            if (itemName.Length <= minLength)
            {
                await ReplyAsync($"Please enter a search term longer than {minLength} characters.").ConfigureAwait(false);
                return;
            }

            if (page < 1)
            {
                await ReplyAsync($"Invalid page number {page}.").ConfigureAwait(false);
                return;
            }

            var matches = ItemParser.GetItemsMatching(itemName, strings).ToArray();

            const int maxLength = 1500;
            const int maxLines = 15;

            var totalPages = (matches.Length + maxLines - 1) / maxLines;

            var ordered = matches
                .OrderBy(z => LevenshteinDistance.Compute(z.Text, itemName))
                .Skip(maxLines * (page - 1))
                .Take(maxLines);

            if (ordered.Count() == 0)
            {
                await ReplyAsync("No matches found.").ConfigureAwait(false);
                return;
            }

            var selected = ordered.Select(z => {
                var msg = $"{z.Value:X4} {z.Text}";
                if (ItemParser.InvertedRecipeDictionary.TryGetValue((ushort)z.Value, out var recipeID))
                {
                    msg += $", DIY: {recipeID:X}000016A2";
                }
                return msg;
            });

            var result = string.Join(Environment.NewLine, selected);
            var pageIndicator = $"Page {page}/{totalPages}";

            if (totalPages > 1)
            {
                result += $"\n{(page != totalPages ? "..." : "")}[{pageIndicator}]";
            }

            if (result.Length > maxLength)
            {
                result = result.Substring(0, maxLength) + "...[truncated]";
            }

            await ReplyAsync(Format.Code(result)).ConfigureAwait(false);
        }

        [Command("item")]
        [Summary("Gets the info for an item.")]
        [RequireQueueRole(nameof(Globals.Self.Config.RoleUseBot))]
        public async Task GetItemInfoAsync([Summary("Item ID (in hex)")] string itemHex)
        {
            ushort itemID = ItemParser.GetID(itemHex);
            if (itemID == Item.NONE)
            {
                await ReplyAsync("Invalid item requested.").ConfigureAwait(false);
                return;
            }

            var name = GameInfo.Strings.GetItemName(itemID);
            var result = ItemInfo.GetItemInfo(itemID);
            if (result.Length == 0)
                await ReplyAsync($"No customization data available for the requested item ({name}).").ConfigureAwait(false);
            else
                await ReplyAsync($"{name}:\r\n{result}").ConfigureAwait(false);
        }

        [Command("stack")]
        [Summary("Stacks an item to max and prints the hex code.")]
        [RequireQueueRole(nameof(Globals.Self.Config.RoleUseBot))]
        public async Task StackAsync([Summary("Item ID (in hex)")] string itemHex)
        {
            ushort itemID = ItemParser.GetID(itemHex);
            bool canStack = ItemInfo.TryGetMaxStackCount(itemID, out ushort maxStack);
            if (itemID == Item.NONE)
            {
                await ReplyAsync("Invalid item requested.").ConfigureAwait(false);
                return;
            }

            if (!canStack)
            {
                await ReplyAsync("Cannot stack.").ConfigureAwait(false);
                return;
            }

            var ct = (ushort)(maxStack - 1);
            var item = new Item(itemID) { Count = ct };
            var msg = ItemParser.GetItemText(item);

            if (maxStack > 1)
            {
                msg += $" (Max: {maxStack})";
            }
            await ReplyAsync(msg).ConfigureAwait(false);
        }
        [Command("stack")]
        [Summary("Stacks an item and prints the hex code.")]
        [RequireQueueRole(nameof(Globals.Self.Config.RoleUseBot))]
        public async Task StackAsync([Summary("Item ID (in hex)")] string itemHex, [Summary("Count of items in the stack")] int count)
        {
            ushort itemID = ItemParser.GetID(itemHex);
            if (itemID == Item.NONE || count < 1 || count > 99)
            {
                await ReplyAsync("Invalid item requested.").ConfigureAwait(false);
                return;
            }

            var ct = count - 1; // value 0 => count of 1
            var item = new Item(itemID) { Count = (ushort)ct };
            var msg = ItemParser.GetItemText(item);
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        [Command("customize")]
        [Summary("Customizes an item and prints the hex code.")]
        [RequireQueueRole(nameof(Globals.Self.Config.RoleUseBot))]
        public async Task CustomizeAsync([Summary("Item ID (in hex)")] string itemHex, [Summary("First customization value")] int cust1, [Summary("Second customization value")] int cust2)
            => await CustomizeAsync(itemHex, cust1 + cust2).ConfigureAwait(false);

        [Command("customize")]
        [Summary("Customizes an item and prints the hex code.")]
        [RequireQueueRole(nameof(Globals.Self.Config.RoleUseBot))]
        public async Task CustomizeAsync([Summary("Item ID (in hex)")] string itemHex, [Summary("Customization value sum")] int sum)
        {
            ushort itemID = ItemParser.GetID(itemHex);
            if (itemID == Item.NONE)
            {
                await ReplyAsync("Invalid item requested.").ConfigureAwait(false);
                return;
            }
            if (sum <= 0)
            {
                await ReplyAsync("No customization data specified.").ConfigureAwait(false);
                return;
            }

            var remake = ItemRemakeUtil.GetRemakeIndex(itemID);
            if (remake < 0)
            {
                await ReplyAsync("No customization data available for the requested item.").ConfigureAwait(false);
                return;
            }

            int body = sum & 7;
            int fabric = sum >> 5;
            if (fabric > 7 || ((fabric << 5) | body) != sum)
            {
                await ReplyAsync("Invalid customization data specified.").ConfigureAwait(false);
                return;
            }

            var info = ItemRemakeInfoData.List[remake];
            // already checked out-of-range body/fabric values above
            bool hasBody = body == 0 || body <= info.ReBodyPatternNum;
            bool hasFabric = fabric == 0 || info.GetFabricDescription(fabric) != "Invalid";

            if (!hasBody || !hasFabric)
                await ReplyAsync("Requested customization for item appears to be invalid.").ConfigureAwait(false);

            var item = new Item(itemID) { BodyType = body, PatternChoice = fabric };
            var msg = ItemParser.GetItemText(item);
            await ReplyAsync(msg).ConfigureAwait(false);
        }
    }
}
