using System.Collections.Generic;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// 差し替え用の絵・音・フォントの置き場所（Assets/CustomAssets）を1か所にまとめた表。
    /// 仮の絵・仮の音を作るツール（Step4〜7・シーンを作るメニュー）は、すべてここに書いた場所を使う。
    /// 場所を変えたいときは、ここだけ直せばよい（ファイルの移動は「Step7：差し替え用フォルダに整理」が行う）。
    /// </summary>
    public static class CustomAssetPaths
    {
        public const string Root = "Assets/CustomAssets";

        // ---- 絵 ----
        public const string Images = Root + "/Images";
        public const string Ingredients = Images + "/Ingredients";
        public const string Room = Images + "/Room";
        public const string Workbench = Images + "/Workbench";
        public const string Liquid = Images + "/Liquid";
        public const string Containers = Images + "/Containers";
        public const string Effects = Images + "/Effects";
        public const string UI = Images + "/UI";
        public const string UICommon = UI + "/Common";
        public const string UIBrew = UI + "/Brew";
        public const string UIDiscovery = UI + "/Discovery";

        // ---- 音 ----
        public const string Sounds = Root + "/Sounds";
        public const string Bgm = Sounds + "/BGM";
        public const string Ambient = Sounds + "/Ambient";
        public const string SE = Sounds + "/SE";
        public const string SeUI = SE + "/UI";
        public const string SeBrewing = SE + "/Brewing";
        public const string SeFeedback = SE + "/Feedback";
        public const string SeShop = SE + "/Shop";
        public const string UnusedSounds = Sounds + "/_Unused";

        // ---- フォント ----
        public const string Fonts = Root + "/Fonts";

        /// <summary>調合台・部屋の絵（ファイル名 → 置き場所）</summary>
        public static readonly Dictionary<string, string> ImageFolders = new Dictionary<string, string>
        {
            { "room_left", Room }, { "room_center", Room }, { "room_right", Room }, { "lamp", Room }, { "ledger_book", Room },
            { "brew_background", Workbench }, { "flask_back", Workbench }, { "flask_front", Workbench }, { "flask_mask", Workbench },
            { "heater_stand", Workbench }, { "knob_base", Workbench }, { "knob_dial", Workbench }, { "flame", Workbench },
            { "stir_rod", Workbench }, { "tray", Workbench },
            { "liquid_body", Liquid }, { "liquid_surface", Liquid }, { "pour_stream", Liquid }, { "drop_piece", Liquid },
            { "container_tall_bottle", Containers }, { "container_tall_bottle_content", Containers },
            { "container_jar", Containers }, { "container_jar_content", Containers },
            { "container_round_flask", Containers }, { "container_round_flask_content", Containers },
            { "container_pouch", Containers }, { "container_pouch_content", Containers },
            { "container_crystal", Containers }, { "container_crystal_content", Containers },
            { "potion_bottle", Containers }, { "potion_bottle_content", Containers },
            { "fx_steam", Effects }, { "fx_bubble", Effects }, { "fx_droplet", Effects }, { "fx_swirl", Effects }, { "fx_ripple", Effects },
        };

        /// <summary>音（ファイル名 → 置き場所）</summary>
        public static readonly Dictionary<string, string> SoundFolders = new Dictionary<string, string>
        {
            { "bgm_title", Bgm }, { "bgm_atelier", Bgm },
            { "amb_room", Ambient }, { "amb_lamp_fire_loop", Ambient },
            { "se_ui_click", SeUI }, { "se_ui_hover", SeUI }, { "se_ui_open", SeUI }, { "se_ui_close", SeUI },
            { "se_page", SeUI }, { "se_start_game", SeUI }, { "se_warning", SeUI },
            { "se_pickup", SeBrewing }, { "se_putback", SeBrewing }, { "se_pour_loop", SeBrewing }, { "se_pour_thin_loop", SeBrewing },
            { "se_plop", SeBrewing }, { "se_drop_solid_1", SeBrewing }, { "se_drop_solid_2", SeBrewing }, { "se_drop_solid_3", SeBrewing },
            { "se_drop_powder_1", SeBrewing }, { "se_drop_powder_2", SeBrewing }, { "se_stir_loop", SeBrewing },
            { "se_stir_tink_1", SeBrewing }, { "se_stir_tink_2", SeBrewing }, { "se_stir_tink_3", SeBrewing },
            { "se_simmer_loop", SeBrewing }, { "se_boil_loop", SeBrewing }, { "se_scorch_loop", SeBrewing }, { "se_burner_loop", SeBrewing },
            { "se_ignite", SeBrewing }, { "se_knob_tick", SeBrewing }, { "se_cork_pop", SeBrewing }, { "se_shelf_place", SeBrewing },
            { "se_brew_special", SeBrewing },
            { "se_koto_knock_1", SeBrewing }, { "se_koto_knock_2", SeBrewing }, { "se_koto_knock_3", SeBrewing },
            { "se_glass_touch_1", SeBrewing }, { "se_glass_touch_2", SeBrewing },
            { "se_star_chime", SeFeedback }, { "se_goal", SeFeedback }, { "se_discovery", SeFeedback },
            { "se_reaction", SeFeedback }, { "se_soft_warning", SeFeedback },
            { "se_door_bell", SeShop }, { "se_voice_blip", SeShop }, { "se_coins", SeShop }, { "se_quest_accept", SeShop },
            { "se_quest_complete", SeShop }, { "se_step_1", SeShop }, { "se_step_2", SeShop }, { "se_step_3", SeShop }, { "se_step_4", SeShop },
            // 今のゲームでは鳴らない（予備）
            { "amb_cauldron", UnusedSounds }, { "se_brew_success", UnusedSounds }, { "se_brew_fail", UnusedSounds }, { "se_discard", UnusedSounds },
        };

        /// <summary>仮の絵の置き場所（表にある名前なら表の場所、なければ fallback）</summary>
        public static string FolderForImage(string name, string fallback)
        {
            return ImageFolders.TryGetValue(name, out string dir) ? dir : fallback;
        }
    }
}
