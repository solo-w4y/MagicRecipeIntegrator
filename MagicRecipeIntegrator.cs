using System;
using System.Reflection;
using System.Collections.Generic;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using MonoMod.RuntimeDetour;
using Microsoft.Xna.Framework;
using MagicStorage;
using MagicStorage.Components;
using Terraria.UI;

namespace MagicRecipeIntegrator
{
    public class MagicRecipeIntegrator : Mod
    {
        public override void Load()
        {
            try
            {
                Logger.Info("MagicRecipeIntegrator: Setting up hook for RecipeBrowser.RecipePath.CalculateHaveItems...");

                var recipePathType = Type.GetType("RecipeBrowser.RecipePath, RecipeBrowser");
                if (recipePathType == null)
                {
                    Logger.Error("RecipeBrowser.RecipePath type not found. Is RecipeBrowser installed?");
                    return;
                }

                var calculateHaveItemsMethod = recipePathType.GetMethod("CalculateHaveItems", BindingFlags.NonPublic | BindingFlags.Static);
                if (calculateHaveItemsMethod == null)
                {
                    Logger.Error("RecipePath.CalculateHaveItems method not found!");
                    return;
                }

                var sourceMagicStorageField = recipePathType.GetField("sourceMagicStorage", BindingFlags.NonPublic | BindingFlags.Static);
                if (sourceMagicStorageField == null)
                {
                    Logger.Warn("sourceMagicStorage field not found. MagicStorage integration will be disabled.");
                }

                MonoModHooks.Add(calculateHaveItemsMethod, new Func<Func<Dictionary<int, int>>, Dictionary<int, int>>(orig =>
                {
                    try
                    {
                        var result = orig();
                        bool sourceMagicStorage = sourceMagicStorageField != null && (bool)sourceMagicStorageField.GetValue(null);
                        if (sourceMagicStorage)
                        {
                            if (!ModLoader.TryGetMod("MagicStorage", out _))
                            {
                                Logger.Warn("MagicStorage mod not found. Skipping integration.");
                                return result;
                            }

                            TEStorageHeart heart = StoragePlayer.LocalPlayer.GetStorageHeart();
                            if (heart != null)
                            {
                                var storedItems = heart.GetStoredItems();
                                if (storedItems != null)
                                {
                                    foreach (var item in storedItems)
                                    {
                                        if (item != null && !item.IsAir && item.type > 0 && item.stack > 0)
                                        {
                                            result.TryGetValue(item.type, out int currentCount);
                                            result[item.type] = currentCount + item.stack;
                                        }
                                    }
                                }
                            }
                        }
                        return result;
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"Error in CalculateHaveItems hook: {e.Message}");
                        Main.NewText("MagicRecipeIntegrator: Error integrating MagicStorage!", Color.Red);
                        return new Dictionary<int, int>();
                    }
                }));

                Logger.Info("MagicRecipeIntegrator: Hook added successfully!");
            }
            catch (Exception e)
            {
                Logger.Error($"Error in Load: {e.Message}");
            }
        }
    }

    public class CheckboxEnablerSystem : ModSystem
    {
        public override void PostSetupContent()
        {
            List<string> checkboxInfo;
            if (!TryEnableCheckbox(out checkboxInfo))
            {
                Mod.Logger.Warn("sourceMagicStorageCheckbox not found.");
            }
        }

        public bool TryEnableCheckbox(out List<string> checkboxInfo)
        {
            checkboxInfo = new List<string>();
            try
            {
                var craftUIType = Type.GetType("RecipeBrowser.CraftUI, RecipeBrowser");
                if (craftUIType == null)
                {
                    Mod.Logger.Warn("CraftUI type not found. Is RecipeBrowser installed?");
                    return false;
                }

                var craftUIField = craftUIType.GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
                if (craftUIField == null || craftUIField.GetValue(null) == null)
                {
                    Mod.Logger.Warn("CraftUI instance not accessible.");
                    return false;
                }

                var mainPanelField = craftUIType.GetField("mainPanel", BindingFlags.Instance | BindingFlags.NonPublic);
                var mainPanel = mainPanelField?.GetValue(craftUIField.GetValue(null)) as UIElement;
                if (mainPanel == null)
                {
                    Mod.Logger.Warn("mainPanel not found.");
                    return false;
                }

                return FindAndEnableCheckbox(mainPanel, checkboxInfo);
            }
            catch (Exception e)
            {
                Mod.Logger.Error($"Error enabling checkbox: {e.Message}");
                return false;
            }
        }

        private bool FindAndEnableCheckbox(UIElement element, List<string> checkboxInfo)
        {
            if (element == null) return false;

            if (element.GetType().Name == "UICheckbox")
            {
                var textProperty = element.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public);
                var hoverTextField = element.GetType().GetField("hoverText", BindingFlags.Instance | BindingFlags.NonPublic);
                string label = textProperty?.GetValue(element) as string;
                string hoverText = hoverTextField?.GetValue(element) as string;

                if (hoverText != null && hoverText.Contains($"{Language.GetTextValue("Mods.RecipeBrowser.CraftUI.MagicStorageTooltip")}"))
                {
                    var setDisabledMethod = element.GetType().GetMethod("SetDisabled", BindingFlags.Instance | BindingFlags.Public);
                    setDisabledMethod?.Invoke(element, new object[] { false });
                    Mod.Logger.Info("sourceMagicStorageCheckbox enabled!");
                    return true;
                }
            }

            foreach (var child in element.Children)
            {
                if (FindAndEnableCheckbox(child, checkboxInfo))
                    return true;
            }
            return false;
        }
    }
}