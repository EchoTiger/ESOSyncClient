--[[
    FissalRelay_KioskRecon.lua
    Autonomous Tamriel Guild Kiosk Tracking & Ground Recon Engine
    Crafted by Echo & Fissal for Fissal Relay and the Redfur Guilds.

    Three-Tier Zero-Sheet Kiosk Recon Architecture:
      1. Automated Guild Finder Multi-Pass Sweep (Zero Porting):
         Iterates Guild Finder categories, alliances, and all languages with
         SetGuildFinderHasTradersSearchFilter(true) via GuildFinderRequestSearch(),
         harvesting GetGuildNameAttribute(guildId) and GetGuildKioskAttribute(guildId).
      2. Incremental Known-Guild ID Prober (Catches 500/500 Full & Unlisted Guilds):
         Polls known PC-NA trading guildIds via RequestGuildFinderAttributesForGuild(guildId)
         on a polite rate-limited timer, reading cached attributes immediately when
         DoesGuildDataHaveInitializedAttributes(guildId, GUILD_META_DATA_ATTRIBUTE_KIOSK) is true.
      3. Passive Reticle, Chatter, Bid Window & Active Bids Harvester:
         Captures owned AND vacant kiosks on reticleover/interact via IsUnitGuildKiosk(unitTag),
         GetUnitGuildKioskOwner(unitTag), and GetUnitCaption(unitTag), plus syncs active
         weekly guild bids via RequestGuildKioskActiveBids(guildId).
]]--

FissalRelay = FissalRelay or {}
local FR = FissalRelay

-- Irregular ESO NPC short names mapped to canonical TraderKiosks names
FR.IRREGULAR_TRADERS = {
    ["eafildil"] = "Rinedel",
    ["zagh"] = "Zagh gro-Stugh",
    ["jaflinna"] = "Jaflinna Snow-born",
    ["var"] = "Var the Vague",
    ["janne jonnicent"] = "Thredis",
    ["fintolorwe"] = "Fintilorwe",
    ["victorie madach"] = "Victoire Madach",
    ["lorthodear"] = "Lorthodaer",
}

-- Canonical 291 Tamriel Guild Traders Directory
FR.KIOSK_DIRECTORY = {
    ["Lejesha"] = { trader = "Lejesha", city = "Bergama", zone = "ALIK'R DESERT", isOutlaw = false },
    ["Manidah"] = { trader = "Manidah", city = "Morwha's Bounty", zone = "ALIK'R DESERT", isOutlaw = false },
    ["Laknar"] = { trader = "Laknar", city = "Sentinel", zone = "ALIK'R DESERT", isOutlaw = false },
    ["Saymimah"] = { trader = "Saymimah", city = "Sentinel", zone = "ALIK'R DESERT", isOutlaw = false },
    ["Uurwaerion"] = { trader = "Uurwaerion", city = "Sentinel", zone = "ALIK'R DESERT", isOutlaw = false },
    ["Vinder Hlaran"] = { trader = "Vinder Hlaran", city = "Sentinel", zone = "ALIK'R DESERT", isOutlaw = false },
    ["Yat"] = { trader = "Yat", city = "Sentinel", zone = "ALIK'R DESERT", isOutlaw = false },
    ["Marbilah"] = { trader = "Marbilah", city = "Sentinel - Refuge", zone = "ALIK'R DESERT", isOutlaw = true },
    ["Panersewen"] = { trader = "Panersewen", city = "Firsthold", zone = "AURIDON", isOutlaw = false },
    ["Cerweriell"] = { trader = "Cerweriell", city = "Skywatch", zone = "AURIDON", isOutlaw = false },
    ["Ferzhela"] = { trader = "Ferzhela", city = "Skywatch", zone = "AURIDON", isOutlaw = false },
    ["Guzg"] = { trader = "Guzg", city = "Skywatch", zone = "AURIDON", isOutlaw = false },
    ["Lanirsare"] = { trader = "Lanirsare", city = "Skywatch", zone = "AURIDON", isOutlaw = false },
    ["Renzaiq"] = { trader = "Renzaiq", city = "Skywatch", zone = "AURIDON", isOutlaw = false },
    ["Carillda"] = { trader = "Carillda", city = "Vulkhel Guard", zone = "AURIDON", isOutlaw = false },
    ["Uraacil"] = { trader = "Uraacil", city = "Vulkhel Guard - Refuge", zone = "AURIDON", isOutlaw = true },
    ["Galam Seleth"] = { trader = "Galam Seleth", city = "Dhalmora", zone = "BAL FOYEN", isOutlaw = false },
    ["Malirzzaka"] = { trader = "Malirzzaka", city = "Bangorai Pass", zone = "BANGKORAI", isOutlaw = false },
    ["Arver Falos"] = { trader = "Arver Falos", city = "Evermore", zone = "BANGKORAI", isOutlaw = false },
    ["Kaale"] = { trader = "Kaale", city = "Evermore", zone = "BANGKORAI", isOutlaw = false },
    ["Tilinarie"] = { trader = "Tilinarie", city = "Evermore", zone = "BANGKORAI", isOutlaw = false },
    ["Values-Many-Things"] = { trader = "Values-Many-Things", city = "Evermore", zone = "BANGKORAI", isOutlaw = false },
    ["Zunlog"] = { trader = "Zunlog", city = "Evermore", zone = "BANGKORAI", isOutlaw = false },
    ["Ornyenque"] = { trader = "Ornyenque", city = "Evermore - Refuge", zone = "BANGKORAI", isOutlaw = true },
    ["Glorgzorgo"] = { trader = "Glorgzorgo", city = "Hallin's Stand", zone = "BANGKORAI", isOutlaw = false },
    ["Ghatrugh"] = { trader = "Ghatrugh", city = "Stonetooth", zone = "BETNIKH", isOutlaw = false },
    ["Amirudda"] = { trader = "Amirudda", city = "Leyawiin", zone = "BLACKWOOD", isOutlaw = false },
    ["Dandras Omayn"] = { trader = "Dandras Omayn", city = "Leyawiin", zone = "BLACKWOOD", isOutlaw = false },
    ["Lhotahir"] = { trader = "Lhotahir", city = "Leyawiin", zone = "BLACKWOOD", isOutlaw = false },
    ["Praxedes Vestalis"] = { trader = "Praxedes Vestalis", city = "Leyawiin", zone = "BLACKWOOD", isOutlaw = false },
    ["Shuruthikh"] = { trader = "Shuruthikh", city = "Leyawiin", zone = "BLACKWOOD", isOutlaw = false },
    ["Sihrimaya"] = { trader = "Sihrimaya", city = "Leyawiin", zone = "BLACKWOOD", isOutlaw = false },
    ["Dion Hassildor"] = { trader = "Dion Hassildor", city = "Leyawiin - Refuge", zone = "BLACKWOOD", isOutlaw = true },
    ["Inishez"] = { trader = "Inishez", city = "Bleakrock", zone = "BLEAKROCK", isOutlaw = false },
    ["Commerce Delegate"] = { trader = "Commerce Delegate", city = "Brass Fortress", zone = "CLOCKWORK CITY", isOutlaw = false },
    ["Noveni Adrano"] = { trader = "Noveni Adrano", city = "Brass Fortress", zone = "CLOCKWORK CITY", isOutlaw = false },
    ["Orstag"] = { trader = "Orstag", city = "Brass Fortress", zone = "CLOCKWORK CITY", isOutlaw = false },
    ["Ravam Sedas"] = { trader = "Ravam Sedas", city = "Brass Fortress", zone = "CLOCKWORK CITY", isOutlaw = false },
    ["Shogarz"] = { trader = "Shogarz", city = "Brass Fortress", zone = "CLOCKWORK CITY", isOutlaw = false },
    ["Valowende"] = { trader = "Valowende", city = "Brass Fortress", zone = "CLOCKWORK CITY", isOutlaw = false },
    ["Nardhil Barys"] = { trader = "Nardhil Barys", city = "Slag Town-Refuge", zone = "CLOCKWORK CITY", isOutlaw = true },
    ["Harzdak"] = { trader = "Harzdak", city = "Court Of Contempt", zone = "COLDHARBOUR", isOutlaw = false },
    ["Shuliish"] = { trader = "Shuliish", city = "Haj Uxith", zone = "COLDHARBOUR", isOutlaw = false },
    ["Balver Sarvani"] = { trader = "Balver Sarvani", city = "Hollow City", zone = "COLDHARBOUR", isOutlaw = false },
    ["Nistyniel"] = { trader = "Nistyniel", city = "Hollow City", zone = "COLDHARBOUR", isOutlaw = false },
    ["Ramzasa"] = { trader = "Ramzasa", city = "Hollow City", zone = "COLDHARBOUR", isOutlaw = false },
    ["Virwillaure"] = { trader = "Virwillaure", city = "Hollow City", zone = "COLDHARBOUR", isOutlaw = false },
    ["Donnaelain"] = { trader = "Donnaelain", city = "Belkarth", zone = "CRAGLORN", isOutlaw = false },
    ["Glegokh"] = { trader = "Glegokh", city = "Belkarth", zone = "CRAGLORN", isOutlaw = false },
    ["Keen-Eyes"] = { trader = "Keen-Eyes", city = "Belkarth", zone = "CRAGLORN", isOutlaw = false },
    ["Mengilwaen"] = { trader = "Mengilwaen", city = "Belkarth", zone = "CRAGLORN", isOutlaw = false },
    ["Nelvon Galen"] = { trader = "Nelvon Galen", city = "Belkarth", zone = "CRAGLORN", isOutlaw = false },
    ["Shelzaka"] = { trader = "Shelzaka", city = "Belkarth", zone = "CRAGLORN", isOutlaw = false },
    ["Shuhasa"] = { trader = "Shuhasa", city = "Belkarth", zone = "CRAGLORN", isOutlaw = false },
    ["Makkhzahr"] = { trader = "Makkhzahr", city = "Belkarth - Refuge", zone = "CRAGLORN", isOutlaw = true },
    ["Endoriell"] = { trader = "Endoriell", city = "Mournhold", zone = "DESHAAN", isOutlaw = false },
    ["Erwurlde"] = { trader = "Erwurlde", city = "Mournhold", zone = "DESHAAN", isOutlaw = false },
    ["Gals Fendyn"] = { trader = "Gals Fendyn", city = "Mournhold", zone = "DESHAAN", isOutlaw = false },
    ["Hayaia"] = { trader = "Hayaia", city = "Mournhold", zone = "DESHAAN", isOutlaw = false },
    ["Razgugul"] = { trader = "Razgugul", city = "Mournhold", zone = "DESHAAN", isOutlaw = false },
    ["Through-Gilded-Eyes"] = { trader = "Through-Gilded-Eyes", city = "Mournhold", zone = "DESHAAN", isOutlaw = false },
    ["Zarum"] = { trader = "Zarum", city = "Mournhold", zone = "DESHAAN", isOutlaw = false },
    ["Nakmargo"] = { trader = "Nakmargo", city = "Mournhold - Refuge", zone = "DESHAAN", isOutlaw = true },
    ["Feran Relenim"] = { trader = "Feran Relenim", city = "Muth Gnaar Hills", zone = "DESHAAN", isOutlaw = false },
    ["Telvon Arobar"] = { trader = "Telvon Arobar", city = "Tal'Deic Grounds", zone = "DESHAAN", isOutlaw = false },
    ["Muslabliz"] = { trader = "Muslabliz", city = "Fort Amol", zone = "EASTMARCH", isOutlaw = false },
    ["Alareth"] = { trader = "Alareth", city = "Voljar Meadery", zone = "EASTMARCH", isOutlaw = false },
    ["Alisewen"] = { trader = "Alisewen", city = "Windhelm", zone = "EASTMARCH", isOutlaw = false },
    ["Celorien"] = { trader = "Celorien", city = "Windhelm", zone = "EASTMARCH", isOutlaw = false },
    ["Deras Golathyn"] = { trader = "Deras Golathyn", city = "Windhelm", zone = "EASTMARCH", isOutlaw = false },
    ["Dosa"] = { trader = "Dosa", city = "Windhelm", zone = "EASTMARCH", isOutlaw = false },
    ["Ghogurz"] = { trader = "Ghogurz", city = "Windhelm", zone = "EASTMARCH", isOutlaw = false },
    ["Meden Berendus"] = { trader = "Meden Berendus", city = "Windhelm - Refuge", zone = "EASTMARCH", isOutlaw = true },
    ["Tuxutl"] = { trader = "Tuxutl", city = "Fargrave - Refuge", zone = "FARGRAVE", isOutlaw = true },
    ["Bodsa Manas"] = { trader = "Bodsa Manas", city = "Fargrave City District", zone = "FARGRAVE", isOutlaw = false },
    ["Furnvekh"] = { trader = "Furnvekh", city = "Fargrave City District", zone = "FARGRAVE", isOutlaw = false },
    ["Livia Tappo"] = { trader = "Livia Tappo", city = "Fargrave City District", zone = "FARGRAVE", isOutlaw = false },
    ["Ven"] = { trader = "Ven", city = "Fargrave City District", zone = "FARGRAVE", isOutlaw = false },
    ["Vesakta"] = { trader = "Vesakta", city = "Fargrave City District", zone = "FARGRAVE", isOutlaw = false },
    ["Zenelaz"] = { trader = "Zenelaz", city = "Fargrave City District", zone = "FARGRAVE", isOutlaw = false },
    ["Arzalaya"] = { trader = "Arzalaya", city = "Vastyr", zone = "GALEN", isOutlaw = false },
    ["Gei"] = { trader = "Gei", city = "Vastyr", zone = "GALEN", isOutlaw = false },
    ["Sharflekh"] = { trader = "Sharflekh", city = "Vastyr", zone = "GALEN", isOutlaw = false },
    ["Stephenn Surilie"] = { trader = "Stephenn Surilie", city = "Vastyr", zone = "GALEN", isOutlaw = false },
    ["Tildinfanya"] = { trader = "Tildinfanya", city = "Vastyr", zone = "GALEN", isOutlaw = false },
    ["Var the Vague"] = { trader = "Var the Vague", city = "Vastyr", zone = "GALEN", isOutlaw = false },
    ["Jahim"] = { trader = "Jahim", city = "Vastyr - Refuge", zone = "GALEN", isOutlaw = true },
    ["Brara Hlaalo"] = { trader = "Brara Hlaalo", city = "Daggerfall", zone = "GLENUMBRA", isOutlaw = false },
    ["Faedre"] = { trader = "Faedre", city = "Daggerfall", zone = "GLENUMBRA", isOutlaw = false },
    ["Khalatah"] = { trader = "Khalatah", city = "Daggerfall", zone = "GLENUMBRA", isOutlaw = false },
    ["Murgoz"] = { trader = "Murgoz", city = "Daggerfall", zone = "GLENUMBRA", isOutlaw = false },
    ["Sintilfalion"] = { trader = "Sintilfalion", city = "Daggerfall", zone = "GLENUMBRA", isOutlaw = false },
    ["Zulgozu"] = { trader = "Zulgozu", city = "Daggerfall - Refuge", zone = "GLENUMBRA", isOutlaw = true },
    ["Nameel"] = { trader = "Nameel", city = "Lionguard Redoubt", zone = "GLENUMBRA", isOutlaw = false },
    ["Mogazgur"] = { trader = "Mogazgur", city = "Wyrd Tree", zone = "GLENUMBRA", isOutlaw = false },
    ["Daynas Sadrano"] = { trader = "Daynas Sadrano", city = "Anvil", zone = "GOLD COAST", isOutlaw = false },
    ["Majhasur"] = { trader = "Majhasur", city = "Anvil", zone = "GOLD COAST", isOutlaw = false },
    ["Onurai-Maht"] = { trader = "Onurai-Maht", city = "Anvil", zone = "GOLD COAST", isOutlaw = false },
    ["Syalleth"] = { trader = "Syalleth", city = "Anvil - Refuge", zone = "GOLD COAST", isOutlaw = true },
    ["Erluramur"] = { trader = "Erluramur", city = "Kvatch", zone = "GOLD COAST", isOutlaw = false },
    ["Farul"] = { trader = "Farul", city = "Kvatch", zone = "GOLD COAST", isOutlaw = false },
    ["Zagh gro-Stugh"] = { trader = "Zagh gro-Stugh", city = "Kvatch", zone = "GOLD COAST", isOutlaw = false },
    ["Nirywy"] = { trader = "Nirywy", city = "Cormount", zone = "GRAHTWOOD", isOutlaw = false },
    ["Bols Thirandus"] = { trader = "Bols Thirandus", city = "Elden Root", zone = "GRAHTWOOD", isOutlaw = false },
    ["Fintilorwe"] = { trader = "Fintilorwe", city = "Elden Root", zone = "GRAHTWOOD", isOutlaw = false },
    ["Goh"] = { trader = "Goh", city = "Elden Root", zone = "GRAHTWOOD", isOutlaw = false },
    ["Iannianith"] = { trader = "Iannianith", city = "Elden Root", zone = "GRAHTWOOD", isOutlaw = false },
    ["Mizul"] = { trader = "Mizul", city = "Elden Root", zone = "GRAHTWOOD", isOutlaw = false },
    ["Naifineh"] = { trader = "Naifineh", city = "Elden Root", zone = "GRAHTWOOD", isOutlaw = false },
    ["Walks-In-Leaves"] = { trader = "Walks-In-Leaves", city = "Elden Root", zone = "GRAHTWOOD", isOutlaw = false },
    ["Naerorien"] = { trader = "Naerorien", city = "Elden Root - Refuge", zone = "GRAHTWOOD", isOutlaw = true },
    ["Glothozug"] = { trader = "Glothozug", city = "Southpoint", zone = "GRAHTWOOD", isOutlaw = false },
    ["Halash"] = { trader = "Halash", city = "Greenheart", zone = "GREENSHADE", isOutlaw = false },
    ["Camyaale"] = { trader = "Camyaale", city = "Marbruk", zone = "GREENSHADE", isOutlaw = false },
    ["Fendros Faryon"] = { trader = "Fendros Faryon", city = "Marbruk", zone = "GREENSHADE", isOutlaw = false },
    ["Ghobargh"] = { trader = "Ghobargh", city = "Marbruk", zone = "GREENSHADE", isOutlaw = false },
    ["Goudadul"] = { trader = "Goudadul", city = "Marbruk", zone = "GREENSHADE", isOutlaw = false },
    ["Hasiwen"] = { trader = "Hasiwen", city = "Marbruk", zone = "GREENSHADE", isOutlaw = false },
    ["Dugugikh"] = { trader = "Dugugikh", city = "Marbruk - Refuge", zone = "GREENSHADE", isOutlaw = true },
    ["Seeks-Better-Deals"] = { trader = "Seeks-Better-Deals", city = "Verrant Morass", zone = "GREENSHADE", isOutlaw = false },
    ["Farvyn Rethan"] = { trader = "Farvyn Rethan", city = "Abah's Landing", zone = "HEWS BANE", isOutlaw = false },
    ["Gathewen"] = { trader = "Gathewen", city = "Abah's Landing", zone = "HEWS BANE", isOutlaw = false },
    ["Qanliz"] = { trader = "Qanliz", city = "Abah's Landing", zone = "HEWS BANE", isOutlaw = false },
    ["Shiny-Trades"] = { trader = "Shiny-Trades", city = "Abah's Landing", zone = "HEWS BANE", isOutlaw = false },
    ["Snegbug"] = { trader = "Snegbug", city = "Abah's Landing", zone = "HEWS BANE", isOutlaw = false },
    ["Virwen"] = { trader = "Virwen", city = "Abah's Landing", zone = "HEWS BANE", isOutlaw = false },
    ["Dahnadreel"] = { trader = "Dahnadreel", city = "Abah's Landing -Thieves Den", zone = "HEWS BANE", isOutlaw = true },
    ["Innryk"] = { trader = "Innryk", city = "Gonfalon Bay", zone = "HIGH ISLE", isOutlaw = false },
    ["Kemshelar"] = { trader = "Kemshelar", city = "Gonfalon Bay", zone = "HIGH ISLE", isOutlaw = false },
    ["Marcelle Fanis"] = { trader = "Marcelle Fanis", city = "Gonfalon Bay", zone = "HIGH ISLE", isOutlaw = false },
    ["Pugereau Laffoon"] = { trader = "Pugereau Laffoon", city = "Gonfalon Bay", zone = "HIGH ISLE", isOutlaw = false },
    ["Shakhrath"] = { trader = "Shakhrath", city = "Gonfalon Bay", zone = "HIGH ISLE", isOutlaw = false },
    ["Zoe Frernile"] = { trader = "Zoe Frernile", city = "Gonfalon Bay", zone = "HIGH ISLE", isOutlaw = false },
    ["Janne Jonnicent"] = { trader = "Janne Jonnicent", city = "Gonfalon Bay - Refuge", zone = "HIGH ISLE", isOutlaw = true },
    ["Dulia"] = { trader = "Dulia", city = "Mistral", zone = "KHENARTHI'S ROOST", isOutlaw = false },
    ["Shamuniz"] = { trader = "Shamuniz", city = "Mistral", zone = "KHENARTHI'S ROOST", isOutlaw = false },
    ["Jalaima"] = { trader = "Jalaima", city = "Bandaari", zone = "MALABAL TOR", isOutlaw = false },
    ["Mani"] = { trader = "Mani", city = "Bandaari", zone = "MALABAL TOR", isOutlaw = false },
    ["Murgrud"] = { trader = "Murgrud", city = "Bandaari", zone = "MALABAL TOR", isOutlaw = false },
    ["Nindenel"] = { trader = "Nindenel", city = "Bandaari", zone = "MALABAL TOR", isOutlaw = false },
    ["Teromawen"] = { trader = "Teromawen", city = "Bandaari", zone = "MALABAL TOR", isOutlaw = false },
    ["Ulyn Marys"] = { trader = "Ulyn Marys", city = "Dra'bul", zone = "MALABAL TOR", isOutlaw = false },
    ["Kharg"] = { trader = "Kharg", city = "Valeguard", zone = "MALABAL TOR", isOutlaw = false },
    ["Galis Andalen"] = { trader = "Galis Andalen", city = "Velyn Harbor - Refuge", zone = "MALABAL TOR", isOutlaw = true },
    ["Aki-Osheeja"] = { trader = "Aki-Osheeja", city = "Lilmoth", zone = "MURKMIRE", isOutlaw = false },
    ["Faelemar"] = { trader = "Faelemar", city = "Lilmoth", zone = "MURKMIRE", isOutlaw = false },
    ["Mahadal at-Bergama"] = { trader = "Mahadal at-Bergama", city = "Lilmoth", zone = "MURKMIRE", isOutlaw = false },
    ["Ordasha"] = { trader = "Ordasha", city = "Lilmoth", zone = "MURKMIRE", isOutlaw = false },
    ["Thaloril"] = { trader = "Thaloril", city = "Lilmoth", zone = "MURKMIRE", isOutlaw = false },
    ["Xokomar"] = { trader = "Xokomar", city = "Lilmoth", zone = "MURKMIRE", isOutlaw = false },
    ["Gir-Ta"] = { trader = "Gir-Ta", city = "Lilmoth - Refuge", zone = "MURKMIRE", isOutlaw = true },
    ["Adiblargo"] = { trader = "Adiblargo", city = "Rimmen", zone = "NORTHERN ELSWEYR", isOutlaw = false },
    ["Artura Pamarc"] = { trader = "Artura Pamarc", city = "Rimmen", zone = "NORTHERN ELSWEYR", isOutlaw = false },
    ["Fortis Asina"] = { trader = "Fortis Asina", city = "Rimmen", zone = "NORTHERN ELSWEYR", isOutlaw = false },
    ["Maelanrith"] = { trader = "Maelanrith", city = "Rimmen", zone = "NORTHERN ELSWEYR", isOutlaw = false },
    ["Nirshala"] = { trader = "Nirshala", city = "Rimmen", zone = "NORTHERN ELSWEYR", isOutlaw = false },
    ["Razzamin"] = { trader = "Razzamin", city = "Rimmen", zone = "NORTHERN ELSWEYR", isOutlaw = false },
    ["Begok"] = { trader = "Begok", city = "Rimmen-Refuge", zone = "NORTHERN ELSWEYR", isOutlaw = true },
    ["Uzarrur"] = { trader = "Uzarrur", city = "Dune", zone = "REAPERS MARCH", isOutlaw = false },
    ["Canda"] = { trader = "Canda", city = "Rawl'kha", zone = "REAPERS MARCH", isOutlaw = false },
    ["Heat-On-Scales"] = { trader = "Heat-On-Scales", city = "Rawl'kha", zone = "REAPERS MARCH", isOutlaw = false },
    ["Muheh"] = { trader = "Muheh", city = "Rawl'kha", zone = "REAPERS MARCH", isOutlaw = false },
    ["Ronuril"] = { trader = "Ronuril", city = "Rawl'kha", zone = "REAPERS MARCH", isOutlaw = false },
    ["Shiniraer"] = { trader = "Shiniraer", city = "Rawl'kha", zone = "REAPERS MARCH", isOutlaw = false },
    ["Sharaddargo"] = { trader = "Sharaddargo", city = "Rawl'kha - Refuge", zone = "REAPERS MARCH", isOutlaw = true },
    ["Ambarys Teran"] = { trader = "Ambarys Teran", city = "Vinedusk", zone = "REAPERS MARCH", isOutlaw = false },
    ["Aldam Urvyn"] = { trader = "Aldam Urvyn", city = "Hoarfrost Downs", zone = "RIVENSPIRE", isOutlaw = false },
    ["Fanwyearie"] = { trader = "Fanwyearie", city = "Oldgate", zone = "RIVENSPIRE", isOutlaw = false },
    ["Frenidela"] = { trader = "Frenidela", city = "Shornhelm", zone = "RIVENSPIRE", isOutlaw = false },
    ["Roudi"] = { trader = "Roudi", city = "Shornhelm", zone = "RIVENSPIRE", isOutlaw = false },
    ["Shakh"] = { trader = "Shakh", city = "Shornhelm", zone = "RIVENSPIRE", isOutlaw = false },
    ["Tendir Vlaren"] = { trader = "Tendir Vlaren", city = "Shornhelm", zone = "RIVENSPIRE", isOutlaw = false },
    ["Vorh"] = { trader = "Vorh", city = "Shornhelm", zone = "RIVENSPIRE", isOutlaw = false },
    ["Bixitleesh"] = { trader = "Bixitleesh", city = "Shornhelm - Refuge", zone = "RIVENSPIRE", isOutlaw = true },
    ["Talen-Dum"] = { trader = "Talen-Dum", city = "Hissmir", zone = "SHADOWFEN", isOutlaw = false },
    ["Emuin"] = { trader = "Emuin", city = "Stormhold", zone = "SHADOWFEN", isOutlaw = false },
    ["Gasheg"] = { trader = "Gasheg", city = "Stormhold", zone = "SHADOWFEN", isOutlaw = false },
    ["Tar-Shehs"] = { trader = "Tar-Shehs", city = "Stormhold", zone = "SHADOWFEN", isOutlaw = false },
    ["Vals Salvani"] = { trader = "Vals Salvani", city = "Stormhold", zone = "SHADOWFEN", isOutlaw = false },
    ["Zino"] = { trader = "Zino", city = "Stormhold", zone = "SHADOWFEN", isOutlaw = false },
    ["Geeh-Sakka"] = { trader = "Geeh-Sakka", city = "Stormhold - Refuge", zone = "SHADOWFEN", isOutlaw = true },
    ["Junal-Nakal"] = { trader = "Junal-Nakal", city = "Venomous Fens", zone = "SHADOWFEN", isOutlaw = false },
    ["Florentina Verus"] = { trader = "Florentina Verus", city = "Sunport", zone = "SOLSTICE", isOutlaw = false },
    ["Gilur Vules"] = { trader = "Gilur Vules", city = "Sunport", zone = "SOLSTICE", isOutlaw = false },
    ["Grobert Agnan"] = { trader = "Grobert Agnan", city = "Sunport", zone = "SOLSTICE", isOutlaw = false },
    ["Mandyl"] = { trader = "Mandyl", city = "Sunport", zone = "SOLSTICE", isOutlaw = false },
    ["Ohanath"] = { trader = "Ohanath", city = "Sunport", zone = "SOLSTICE", isOutlaw = false },
    ["Tuhdri"] = { trader = "Tuhdri", city = "Sunport", zone = "SOLSTICE", isOutlaw = false },
    ["Fanyehha"] = { trader = "Fanyehha", city = "Sunport - Refuge", zone = "SOLSTICE", isOutlaw = true },
    ["Glaetaldo"] = { trader = "Glaetaldo", city = "Senchal", zone = "SOUTHERN ELSWEYR", isOutlaw = false },
    ["Golgakul"] = { trader = "Golgakul", city = "Senchal", zone = "SOUTHERN ELSWEYR", isOutlaw = false },
    ["Jaflinna Snow-born"] = { trader = "Jaflinna Snow-born", city = "Senchal", zone = "SOUTHERN ELSWEYR", isOutlaw = false },
    ["Maguzak"] = { trader = "Maguzak", city = "Senchal", zone = "SOUTHERN ELSWEYR", isOutlaw = false },
    ["Saden Sarvani"] = { trader = "Saden Sarvani", city = "Senchal", zone = "SOUTHERN ELSWEYR", isOutlaw = false },
    ["Wusava"] = { trader = "Wusava", city = "Senchal", zone = "SOUTHERN ELSWEYR", isOutlaw = false },
    ["Laytiva Sendris"] = { trader = "Laytiva Sendris", city = "Senchal - Refuge", zone = "SOUTHERN ELSWEYR", isOutlaw = true },
    ["Tanur Llervu"] = { trader = "Tanur Llervu", city = "Davon's Watch", zone = "STONEFALLS", isOutlaw = false },
    ["Adagwen"] = { trader = "Adagwen", city = "Davon's Watch - Refuge", zone = "STONEFALLS", isOutlaw = true },
    ["Gananith"] = { trader = "Gananith", city = "Ebonheart", zone = "STONEFALLS", isOutlaw = false },
    ["J'zaraer"] = { trader = "J'zaraer", city = "Ebonheart", zone = "STONEFALLS", isOutlaw = false },
    ["Luz"] = { trader = "Luz", city = "Ebonheart", zone = "STONEFALLS", isOutlaw = false },
    ["Silver-Scales"] = { trader = "Silver-Scales", city = "Ebonheart", zone = "STONEFALLS", isOutlaw = false },
    ["Urvel Hlaren"] = { trader = "Urvel Hlaren", city = "Ebonheart", zone = "STONEFALLS", isOutlaw = false },
    ["Ma'jidid"] = { trader = "Ma'jidid", city = "Kragenmoor", zone = "STONEFALLS", isOutlaw = false },
    ["Dromash"] = { trader = "Dromash", city = "Firebrand Keep", zone = "STORMHAVEN", isOutlaw = false },
    ["Aniama"] = { trader = "Aniama", city = "Koeglin Village", zone = "STORMHAVEN", isOutlaw = false },
    ["Aerchith"] = { trader = "Aerchith", city = "Wayrest", zone = "STORMHAVEN", isOutlaw = false },
    ["Ah-Zish"] = { trader = "Ah-Zish", city = "Wayrest", zone = "STORMHAVEN", isOutlaw = false },
    ["Atin"] = { trader = "Atin", city = "Wayrest", zone = "STORMHAVEN", isOutlaw = false },
    ["Azarati"] = { trader = "Azarati", city = "Wayrest", zone = "STORMHAVEN", isOutlaw = false },
    ["Estilldo"] = { trader = "Estilldo", city = "Wayrest", zone = "STORMHAVEN", isOutlaw = false },
    ["Morg"] = { trader = "Morg", city = "Wayrest", zone = "STORMHAVEN", isOutlaw = false },
    ["Tredyn Daram"] = { trader = "Tredyn Daram", city = "Wayrest", zone = "STORMHAVEN", isOutlaw = false },
    ["Essilion"] = { trader = "Essilion", city = "Wayrest - Refuge", zone = "STORMHAVEN", isOutlaw = true },
    ["Makmargo"] = { trader = "Makmargo", city = "Port Hunding", zone = "STROS M'KAI", isOutlaw = false },
    ["Huzzin"] = { trader = "Huzzin", city = "Alinor", zone = "SUMMERSET", isOutlaw = false },
    ["Irna Dren"] = { trader = "Irna Dren", city = "Alinor", zone = "SUMMERSET", isOutlaw = false },
    ["Rialilrin"] = { trader = "Rialilrin", city = "Alinor", zone = "SUMMERSET", isOutlaw = false },
    ["Rubyn Denile"] = { trader = "Rubyn Denile", city = "Alinor", zone = "SUMMERSET", isOutlaw = false },
    ["Talwullaure"] = { trader = "Talwullaure", city = "Alinor", zone = "SUMMERSET", isOutlaw = false },
    ["Yggurz Strongbow"] = { trader = "Yggurz Strongbow", city = "Alinor", zone = "SUMMERSET", isOutlaw = false },
    ["Utzaei"] = { trader = "Utzaei", city = "Alinor-Refuge", zone = "SUMMERSET", isOutlaw = true },
    ["Ambalor"] = { trader = "Ambalor", city = "Lillandril", zone = "SUMMERSET", isOutlaw = false },
    ["Nowajan"] = { trader = "Nowajan", city = "Lillandril", zone = "SUMMERSET", isOutlaw = false },
    ["Rinedel"] = { trader = "Rinedel", city = "Lillandril", zone = "SUMMERSET", isOutlaw = false },
    ["Quelilmor"] = { trader = "Quelilmor", city = "Shimmerene", zone = "SUMMERSET", isOutlaw = false },
    ["Shargalash"] = { trader = "Shargalash", city = "Shimmerene", zone = "SUMMERSET", isOutlaw = false },
    ["Varandia"] = { trader = "Varandia", city = "Shimmerene", zone = "SUMMERSET", isOutlaw = false },
    ["Alvura Thenim"] = { trader = "Alvura Thenim", city = "Necrom", zone = "TELVANNI PENINSULA", isOutlaw = false },
    ["Falani"] = { trader = "Falani", city = "Necrom", zone = "TELVANNI PENINSULA", isOutlaw = false },
    ["Grudogg"] = { trader = "Grudogg", city = "Necrom", zone = "TELVANNI PENINSULA", isOutlaw = false },
    ["Runethyne Brenur"] = { trader = "Runethyne Brenur", city = "Necrom", zone = "TELVANNI PENINSULA", isOutlaw = false },
    ["Tuls Madryon"] = { trader = "Tuls Madryon", city = "Necrom", zone = "TELVANNI PENINSULA", isOutlaw = false },
    ["Wyn Serpe"] = { trader = "Wyn Serpe", city = "Necrom", zone = "TELVANNI PENINSULA", isOutlaw = false },
    ["Thredis"] = { trader = "Thredis", city = "Necrom - Refuge", zone = "TELVANNI PENINSULA", isOutlaw = true },
    ["Bodfira"] = { trader = "Bodfira", city = "Markarth", zone = "THE REACH", isOutlaw = false },
    ["Brighortan"] = { trader = "Brighortan", city = "Markarth", zone = "THE REACH", isOutlaw = false },
    ["Jotep-Mota"] = { trader = "Jotep-Mota", city = "Markarth", zone = "THE REACH", isOutlaw = false },
    ["Keltorgan"] = { trader = "Keltorgan", city = "Markarth", zone = "THE REACH", isOutlaw = false },
    ["Marilia Verethi"] = { trader = "Marilia Verethi", city = "Markarth", zone = "THE REACH", isOutlaw = false },
    ["Victoire Madach"] = { trader = "Victoire Madach", city = "Markarth", zone = "THE REACH", isOutlaw = false },
    ["Morborgol"] = { trader = "Morborgol", city = "Markarth - Refuge", zone = "THE REACH", isOutlaw = true },
    ["Atheval"] = { trader = "Atheval", city = "Nimalten", zone = "THE RIFT", isOutlaw = false },
    ["Arnyeana"] = { trader = "Arnyeana", city = "Riften", zone = "THE RIFT", isOutlaw = false },
    ["Eralian"] = { trader = "Eralian", city = "Riften", zone = "THE RIFT", isOutlaw = false },
    ["Jeelus-Lei"] = { trader = "Jeelus-Lei", city = "Riften", zone = "THE RIFT", isOutlaw = false },
    ["Llether Nilem"] = { trader = "Llether Nilem", city = "Riften", zone = "THE RIFT", isOutlaw = false },
    ["Parvaia"] = { trader = "Parvaia", city = "Riften", zone = "THE RIFT", isOutlaw = false },
    ["Majdawa"] = { trader = "Majdawa", city = "Riften - Refuge", zone = "THE RIFT", isOutlaw = true },
    ["Adainji"] = { trader = "Adainji", city = "Shor's Stone", zone = "THE RIFT", isOutlaw = false },
    ["Ginette Malarelie"] = { trader = "Ginette Malarelie", city = "Balmora", zone = "VVARDENFELL", isOutlaw = false },
    ["Mahrahdr"] = { trader = "Mahrahdr", city = "Balmora", zone = "VVARDENFELL", isOutlaw = false },
    ["Narril"] = { trader = "Narril", city = "Balmora", zone = "VVARDENFELL", isOutlaw = false },
    ["Felayn Uvaram"] = { trader = "Felayn Uvaram", city = "Sadrith Mora", zone = "VVARDENFELL", isOutlaw = false },
    ["Runik"] = { trader = "Runik", city = "Sadrith Mora", zone = "VVARDENFELL", isOutlaw = false },
    ["Ruxultav"] = { trader = "Ruxultav", city = "Sadrith Mora", zone = "VVARDENFELL", isOutlaw = false },
    ["Atazha"] = { trader = "Atazha", city = "Vivec City", zone = "VVARDENFELL", isOutlaw = false },
    ["Jena Calvus"] = { trader = "Jena Calvus", city = "Vivec City", zone = "VVARDENFELL", isOutlaw = false },
    ["Lorthodaer"] = { trader = "Lorthodaer", city = "Vivec City", zone = "VVARDENFELL", isOutlaw = false },
    ["Mauhoth"] = { trader = "Mauhoth", city = "Vivec City", zone = "VVARDENFELL", isOutlaw = false },
    ["Rinami"] = { trader = "Rinami", city = "Vivec City", zone = "VVARDENFELL", isOutlaw = false },
    ["Sebastian Brutya"] = { trader = "Sebastian Brutya", city = "Vivec City", zone = "VVARDENFELL", isOutlaw = false },
    ["Relieves-Burdens"] = { trader = "Relieves-Burdens", city = "Vivec City - Refuge", zone = "VVARDENFELL", isOutlaw = true },
    ["Bezaiq"] = { trader = "Bezaiq", city = "Skingrad", zone = "WEST WEALD", isOutlaw = false },
    ["Irg"] = { trader = "Irg", city = "Skingrad", zone = "WEST WEALD", isOutlaw = false },
    ["Jabariq"] = { trader = "Jabariq", city = "Skingrad", zone = "WEST WEALD", isOutlaw = false },
    ["Synnolian Entius"] = { trader = "Synnolian Entius", city = "Skingrad", zone = "WEST WEALD", isOutlaw = false },
    ["Ushataga"] = { trader = "Ushataga", city = "Skingrad", zone = "WEST WEALD", isOutlaw = false },
    ["Vaelelanda"] = { trader = "Vaelelanda", city = "Skingrad", zone = "WEST WEALD", isOutlaw = false },
    ["Amwuana"] = { trader = "Amwuana", city = "Skingrad - Refuge", zone = "WEST WEALD", isOutlaw = true },
    ["Bredromathor"] = { trader = "Bredromathor", city = "Solitude", zone = "WESTERN SKYRIM", isOutlaw = false },
    ["Huleida"] = { trader = "Huleida", city = "Solitude", zone = "WESTERN SKYRIM", isOutlaw = false },
    ["Maevolk"] = { trader = "Maevolk", city = "Solitude", zone = "WESTERN SKYRIM", isOutlaw = false },
    ["Trallinarian"] = { trader = "Trallinarian", city = "Solitude", zone = "WESTERN SKYRIM", isOutlaw = false },
    ["Zuugozag"] = { trader = "Zuugozag", city = "Solitude", zone = "WESTERN SKYRIM", isOutlaw = false },
    ["M'deshargo"] = { trader = "M'deshargo", city = "Solitude - Refuge", zone = "WESTERN SKYRIM", isOutlaw = true },
    ["Wita"] = { trader = "Wita", city = "Dragon Bridge", zone = "WESTERN SKYRIM", isOutlaw = false },
    ["Borgrara"] = { trader = "Borgrara", city = "Morkul Stronghold", zone = "WROTHGAR", isOutlaw = false },
    ["Henriette Panoit"] = { trader = "Henriette Panoit", city = "Morkul Stronghold", zone = "WROTHGAR", isOutlaw = false },
    ["Nagrul gro-Stugbaz"] = { trader = "Nagrul gro-Stugbaz", city = "Morkul Stronghold", zone = "WROTHGAR", isOutlaw = false },
    ["Oorgurn"] = { trader = "Oorgurn", city = "Morkul Stronghold", zone = "WROTHGAR", isOutlaw = false },
    ["Jee-Ma"] = { trader = "Jee-Ma", city = "Orsinium", zone = "WROTHGAR", isOutlaw = false },
    ["Lianorien"] = { trader = "Lianorien", city = "Orsinium", zone = "WROTHGAR", isOutlaw = false },
    ["Logogru"] = { trader = "Logogru", city = "Orsinium", zone = "WROTHGAR", isOutlaw = false },
    ["Mabit"] = { trader = "Mabit", city = "Orsinium", zone = "WROTHGAR", isOutlaw = false },
    ["Mervs Sarys"] = { trader = "Mervs Sarys", city = "Orsinium", zone = "WROTHGAR", isOutlaw = false },
    ["Terorne"] = { trader = "Terorne", city = "Orsinium", zone = "WROTHGAR", isOutlaw = false },
    ["Narkhukulg"] = { trader = "Narkhukulg", city = "Orsinium - Refuge", zone = "WROTHGAR", isOutlaw = true },
}

-- Seed PC-NA Trading Guild Registry (636 known trading guilds)
FR.SEED_TRADING_GUILDS = {
    [175] = "Óðinn's Úlfhéðnar",
    [535] = "East Empire Trading Co",
    [697] = "The Auction House",
    [1119] = "The Noore",
    [1173] = "Grievance",
    [1243] = "Breakpoint Tactics",
    [1279] = "The Ska'vyn Exchange",
    [1427] = "Black Market Wares",
    [1585] = "Almalexia's Amazing Deals",
    [1627] = "Elder Moot",
    [1941] = "Mad Four Destruction",
    [2273] = "Khans Legion",
    [2627] = "Dreadlords",
    [2707] = "Vanquish",
    [3051] = "Eyes of the Queen",
    [3555] = "Time is Money",
    [3611] = "Lost Relic",
    [4143] = "Old Timers Trading",
    [4407] = "Great House Hlaalu",
    [4441] = "Nightshade",
    [4993] = "UESP",
    [7147] = "Royal Black Watch",
    [7369] = "Ender's Jeesh",
    [7687] = "Immortal Empire",
    [11599] = "Inquisitio",
    [13035] = "The Sidekick Order",
    [13125] = "Shades of Sithis",
    [21341] = "Wreckin Ball",
    [21505] = "Skooma Dealers",
    [23467] = "Legion of the Forgotten",
    [26279] = "Dragon's Nest Thievery Co",
    [33279] = "Breaker Breaker",
    [34431] = "The Mercantile",
    [35979] = "Mortiferum",
    [41505] = "The Silk Road",
    [42285] = "Tamriel Black Market",
    [49913] = "Tamriel Stock Exchange",
    [56689] = "Night Masks",
    [57975] = "Iron Forge AH",
    [61555] = "Children of Hircine",
    [73861] = "Eternal Spirits",
    [77287] = "Stewards of Tamriel",
    [84115] = "Tamriel Emporium",
    [86881] = "Peaky Blinders",
    [88063] = "Hlaalu Trading Company",
    [88777] = "Alls Faire Trading Guild",
    [91215] = "Nights Revival",
    [92475] = "Dominion Imperial Guard",
    [95193] = "JPN Trade Center",
    [109425] = "The Wandering Merchants",
    [111111] = "The Conclave of Shadows",
    [113141] = "Ye olde guard",
    [121483] = "For Gain And Glory",
    [124923] = "Sabre Trading Company",
    [125935] = "SoldiersOfTheBrokenEmpire",
    [127263] = "Trusty Skeever Emporium",
    [130457] = "Rum and Wreckage",
    [139559] = "Call To Glory",
    [139655] = "Ethereal Traders Union",
    [142149] = "WARmart Blade'n'Trade",
    [144227] = "Fairy Tail of Tamriel",
    [145413] = "Lucian Allegiance",
    [150205] = "Market Palace",
    [150511] = "Wolves of Fenris",
    [151817] = "Lone Wolf Help",
    [156079] = "Loots R Us Trading Guild",
    [160163] = "Bull City Tradesman",
    [162077] = "Victory or Valhalla",
    [163267] = "Guardian's Covenant",
    [163775] = "Paradox Raiding",
    [170025] = "Red Nirn Reserve",
    [175549] = "Lords of the Storm",
    [176943] = "Dark Wizards",
    [178087] = "Craftaholics",
    [181041] = "Iron Bank of Bravos",
    [182779] = "Hoarders Anonymous",
    [183579] = "Royal Bank of Tamriel",
    [184913] = "Elder Scrolls Exchange",
    [188969] = "Dead Nirn Dealers",
    [190805] = "The Flock",
    [193245] = "The Dark Merchants",
    [195791] = "Bleakrock Barter Co",
    [198789] = "Fundies in Undies",
    [201577] = "The Red Rose Sisterhood",
    [203165] = "Gold Dragons",
    [203711] = "Imperial Trade Union",
    [204261] = "Amazing Deals of Tamriel",
    [205931] = "Bright Forge",
    [206025] = "Savage Blade",
    [206169] = "Bat-Country",
    [209271] = "Valheru's",
    [211509] = "Trans-Tamriel Trade n Aid",
    [211693] = "Glory Guts and Gold",
    [212001] = "Scheming Skeever Imports",
    [215153] = "Ethereal Traders Union II",
    [216697] = "The MoonSugar Candy Co",
    [216823] = "COEUS",
    [218387] = "Tamriel College",
    [219409] = "Omerta Grove",
    [219469] = "Ruin Of Praag",
    [222399] = "Tamriel Traders Exchange",
    [222637] = "Scourge Alliance",
    [226123] = "Pacrooti's Hirelings",
    [229811] = "T Co-The Ascended Order",
    [231665] = "Ghost Sea Trading Co",
    [233931] = "Beal Brotherhood",
    [235463] = "Bal-Busters",
    [242589] = "Rakk's Accoutrements",
    [244189] = "Pilfering Peddlers",
    [264915] = "ebonheart merchant",
    [266193] = "Blackbriar Barter Co",
    [271033] = "Remnants of Shadow",
    [274977] = "Order of The Golden Moons",
    [276507] = "Oblivion's Orphans",
    [276637] = "B Co-The Ascended Order",
    [285643] = "League of Warriors",
    [294067] = "The Ghosts of the Sea",
    [296809] = "The Pursuit Of Happiness",
    [298209] = "AK Tamriel Trade",
    [303181] = "The Ninja Squirrels",
    [305243] = "Tamriel Fisheries",
    [310209] = "The Meat Market",
    [311535] = "Midnight Cartel",
    [313655] = "Barely Lethal",
    [317185] = "Traders of the Lost Doom",
    [321279] = "Stendarrs Watch",
    [321439] = "Amazing Deals Of Nirn",
    [326235] = "AssemblyOfSafetyNSecurity",
    [326527] = "The Disenfranchised",
    [326755] = "Skyrim Merchant Marines",
    [330241] = "Azura's Guidance",
    [332999] = "Sempiternal",
    [334271] = "As Above So Below",
    [335837] = "SneakyBO",
    [337965] = "Golden Light",
    [338361] = "AngelsxDemons",
    [338579] = "Truckers Corner",
    [342917] = "Maxwell House Gamers",
    [343943] = "Senche Adventure Academy",
    [344379] = "Spicy Economics",
    [344653] = "Heroes of the Pact",
    [345753] = "Rusty Old Dragons",
    [347967] = "The Free Trade Agreement",
    [348049] = "The Northern Lights",
    [348647] = "CUC Traders United",
    [357457] = "Chaos Riders",
    [366011] = "Imperium of the Eagle",
    [370167] = "Auction House Central",
    [371245] = "Moon-Sugar Shrimp",
    [375495] = "Ancient Traders",
    [380869] = "Blue Dragon Company",
    [384165] = "Elite Warlords",
    [390383] = "The Dragon's Apothecary",
    [391101] = "Moon's Edge",
    [393407] = "The Hive Mind",
    [403505] = "The Conquest of Tamriel",
    [405297] = "Forever Trading",
    [407373] = "Tal'Vodin",
    [410155] = "Blood Moon Traders",
    [414775] = "Kitty Rainbow Dash",
    [415793] = "Silverlight Brotherhood",
    [416903] = "OneStrong",
    [418701] = "Japan Players Network",
    [423421] = "Once Upon a Time",
    [429141] = "Tamriel Adventurers Guild",
    [429515] = "CloudNINE Superstore",
    [429597] = "Imperial Gold Reserve",
    [429955] = "Divine Crusade",
    [436493] = "Mirkwood Forest",
    [437013] = "Blue Dragon Rose",
    [439541] = "Food and Stuff",
    [441569] = "Bläck Fläg",
    [441797] = "Dirty Secrets",
    [444225] = "The Lost Wayfarers",
    [446037] = "Divine Design Alliance",
    [446653] = "ThievesAndLovers",
    [447377] = "The Medium Place",
    [448117] = "Cheerful Slaughter Circus",
    [448523] = "Dark Arts and Crafts",
    [449835] = "Screw the Queue",
    [453285] = "The Nirncrack Traffickers",
    [455789] = "Outlaw Merchant Society",
    [461993] = "Unmitigated Badassery",
    [468731] = "Made in Canada",
    [476157] = "Feline Good Meowporium",
    [476977] = "Cottage of Cheese",
    [478605] = "Death Wholesale",
    [480813] = "Night Mother's Kiss",
    [481163] = "Free Marketers",
    [482441] = "Swamp Haven",
    [484125] = "Penchant for Chaos",
    [484413] = "Phoenix Emporium",
    [484997] = "At Writs End",
    [485347] = "Daggerfall Emporium Inc",
    [486239] = "Dark Crafting INC",
    [487651] = "Accalias Pride",
    [492935] = "The Elderly Scrolls",
    [495001] = "Bacon Rats",
    [496589] = "Drunk Dungeon Crasher's",
    [502381] = "Khajiit of the Light",
    [504447] = "Real Guild Best Guild",
    [505511] = "Dabs and Dungeons",
    [505915] = "DED BLOODMARKET",
    [508255] = "RawlMart",
    [509785] = "Akatosh Trading Company",
    [510563] = "SaKage",
    [511643] = "Dark Heroes of Nirn",
    [511919] = "Brotherhood Of Rice",
    [516547] = "The Cheeky Ravens",
    [526185] = "Ghosts Of The IX",
    [528827] = "Tamriel Buy and Sell",
    [529313] = "What a Mess",
    [534809] = "The Blackfeather Court",
    [534979] = "The Dragon Lair",
    [535325] = "The Heroic Allies of Nirn",
    [535495] = "Brightstar",
    [536021] = "Available Trader Kiosk",
    [536975] = "The Loreseekers",
    [537495] = "The Golden Mudcrab",
    [539267] = "Chimera Syndicate",
    [542355] = "Killer Rainbow Dash",
    [544411] = "Lords Of Righteousness",
    [544483] = "Dominion Special Forces",
    [544633] = "Blue Boar",
    [544927] = "The Cutthroat Mutineers",
    [547151] = "Guardians of the Mammoth",
    [547475] = "Imperfect Cleave",
    [549837] = "Combat Arms",
    [553981] = "The Lonely Adventurers",
    [556963] = "Pyandonean Bottle Company",
    [558257] = "Taste of Poison",
    [561401] = "Conquest of Tamriel II",
    [563447] = "Section-Zero",
    [567157] = "Honor Guard of Tamriel",
    [573847] = "Golden Fuzzy Dragons",
    [575137] = "Spicy Life",
    [577731] = "Black Rose Knights",
    [580745] = "Feline Great Meowporium",
    [583627] = "Hellø Kitty ESØAdventures",
    [584827] = "Loots Are Yours",
    [585821] = "Stop-N-Loot",
    [592467] = "Mammoth Trading Company",
    [594997] = "Nirns Absolute Best",
    [595291] = "TravelingTorchbug Traders",
    [597845] = "Solo Players Association",
    [599319] = "Bridge Sleeper Cult",
    [600103] = "The Samurai Squirrels",
    [600861] = "West Empire Trading Co",
    [602611] = "Awesome Sàuce",
    [610555] = "I'kiia Home Furnishing",
    [611863] = "Lion Knights",
    [615557] = "Sancrelyeis Avenue Market",
    [616323] = "Luminous Legion",
    [617463] = "Strength In Numbers",
    [618553] = "Aedra",
    [619313] = "TravelingTorchbugs United",
    [619315] = "The Torchbug Exchange",
    [619575] = "TazzyDevils",
    [620405] = "The Skooma Emporium",
    [622389] = "Unstable Unicorns",
    [622427] = "Five Finger Trading",
    [624859] = "Nirn's Guardians",
    [625833] = "The Trading Council",
    [627497] = "The Ring of the Lords",
    [627523] = "La Taverne ESO Qc",
    [627689] = "Kilts For Sale",
    [634031] = "My lovely Nocturnal",
    [634907] = "Gold Coast Corsairs",
    [635297] = "Monalyssa",
    [635655] = "Trials and Error",
    [636855] = "Orphans No More",
    [637705] = "Celestial Estate",
    [639371] = "Automaton Ayrenn's Agents",
    [645463] = "Tamriel Hit Squad",
    [646927] = "Free Spirits",
    [648815] = "Legion of Dragon Slayers",
    [649467] = "Conquest of Tamriel IIl",
    [651311] = "St Elsweyr Guild",
    [653725] = "Aetherial Wayfarers",
    [653799] = "Solus Novo",
    [658075] = "Sheogorath's pals",
    [659369] = "Ayrenn's Army",
    [660081] = "Anamagus",
    [660663] = "Splendid Achievers",
    [662339] = "Wildfire-Gaming",
    [663303] = "Faerie Realm",
    [663531] = "Heaven Above Auridon",
    [664125] = "Shadows of Despair",
    [664447] = "DungeonCrawlingCrafters",
    [665471] = "Pigeon Legion",
    [666513] = "Pixel Pirates",
    [666611] = "Purple Button Horde",
    [668429] = "Transcending Apotheosis",
    [669927] = "The Sweetroll Brigade",
    [674573] = "The Brothers of Tamriel",
    [676775] = "DragonDucks",
    [677213] = "Mad Tinkerer's Tavern",
    [678247] = "The Aetherial Bandits",
    [679737] = "Siroriaa Siroria Noo",
    [679851] = "The Horde",
    [680699] = "One More Pull",
    [684135] = "The Sweaty Casuals",
    [685861] = "Waterlogged PsijicSatchel",
    [686979] = "Avatars Next Generation",
    [687267] = "Hircine Hunters",
    [687581] = "Moon Hunter Keep",
    [688671] = "Klub Kerfuffle",
    [691411] = "Toss A Coin",
    [692585] = "Brazilian Blood",
    [692651] = "Vulcan Commandos",
    [692737] = "Keep Kind",
    [694797] = "ÆRO",
    [694875] = "Immoral Trading",
    [695439] = "TheLastPackrats",
    [696585] = "St Greymoor Guild",
    [696623] = "Weekend Wizards",
    [698471] = "Dragonfly Emporium",
    [698893] = "The Descendants",
    [699051] = "Thirsty Mephitidae",
    [699833] = "Trials and Dungeons",
    [700291] = "Walks-the-Uncharted",
    [700411] = "Oops I Pulled",
    [701479] = "Status Anxiety",
    [702037] = "Groovy Toons of Tamriel",
    [702647] = "DED OUTLAWS ASSOCIATION",
    [703117] = "The Bloodstone Knights",
    [705263] = "Piston",
    [706249] = "Aether Realm",
    [706861] = "Tamriel Trade Ministry",
    [708077] = "SGLPD",
    [709141] = "LUX-URIOUS GOODS",
    [710153] = "The Kiingdom",
    [710611] = "Chaos Crux",
    [710635] = "LionsGild",
    [710661] = "Gates of Vith",
    [711249] = "Maxwell House Guards",
    [711477] = "The Potato Kingdom",
    [712133] = "House Omerta",
    [712185] = "Fus Ro QUACK",
    [712367] = "La Orden de Akatosh",
    [712389] = "Loreseekers Reliquary",
    [712635] = "Cadaverous Assassins",
    [713749] = "The Wild Hunt",
    [714019] = "DED SICA ZUZECA",
    [715455] = "Shadow's Exchange",
    [716345] = "Greymoor Ravagers",
    [716371] = "PALS",
    [717935] = "Solitude Guards",
    [719947] = "Maxwell House Gourmets",
    [720529] = "BeamMeUp-Four",
    [721047] = "Mystical Dark Star",
    [721791] = "Suite Emotions",
    [722031] = "Tipsy Together",
    [722211] = "Arrow To The Knee Lounge",
    [723087] = "NEKO DOUMEI",
    [724495] = "Black Sheep Brigade",
    [725655] = "Butterscotch Dragon Frog",
    [726555] = "JOOB NATION",
    [727081] = "Serenity's Haven",
    [727399] = "The Treasure Hunters",
    [729293] = "Mighty Meow's Myrmidons",
    [729625] = "Happy Feet",
    [730237] = "Fun House",
    [730397] = "The Shivering Cheese",
    [731243] = "Spicy Trial Mix",
    [731305] = "It'sAFeature",
    [731501] = "Auld Souls",
    [732163] = "Fort Knocks Trading",
    [734393] = "x Pøtatø Pact x",
    [736071] = "Ankle Biters",
    [738493] = "Masser's Merchants",
    [740119] = "ALL STARS",
    [741017] = "Everlasting Vale",
    [742207] = "Coldharbour's Guild",
    [744013] = "Gates of Vith ll",
    [749405] = "No Drama Llamas",
    [750281] = "Nirn Warrior's Of Tamriel",
    [750713] = "Stardust of Sithis",
    [750797] = "Darkwater",
    [750837] = "The Great House Telvanni",
    [752827] = "Immoral Traderz",
    [752869] = "TripleHoeTrading",
    [753203] = "Cadwells Army",
    [755561] = "Vampiric Dungeoneers",
    [756059] = "Aedric",
    [757283] = "Warm Sands and Cool Water",
    [757439] = "Úlfheðnar",
    [759181] = "I'kiia Home Goods",
    [760403] = "Swornsword",
    [761377] = "Fredas Drunken Guar",
    [761629] = "The Chill Syndicate",
    [761771] = "Order of Thorn",
    [761817] = "Alphabet Mafia",
    [761967] = "Alt Bank Collective",
    [763335] = "Children of Corruption",
    [763597] = "La Garde Noire",
    [763623] = "Straggler Rally",
    [765167] = "Lords of the Pact",
    [766671] = "The Fleeting Vestige",
    [767683] = "Black Market Trade Group",
    [767717] = "The Alpacalypse",
    [767919] = "The After Life",
    [768263] = "Alibaba",
    [769189] = "Rangers Tavern",
    [772005] = "Meanness",
    [772445] = "Beyond The Pale Order",
    [774741] = "The Lead Foragers",
    [775593] = "Mundus Mundi",
    [776387] = "Mages Guild Drop Outs",
    [776459] = "Guar-Mart",
    [777099] = "Gold of Nirn",
    [777143] = "The Great House Redoran",
    [777789] = "Krow Kult",
    [779069] = "The Shadow Bazaar",
    [780041] = "BIG BEAN BURRITOS",
    [781641] = "Pride of Ebon",
    [782277] = "Tribo dos Carecas",
    [782329] = "Oblivious Raiders",
    [782479] = "x Baked Pøtatøes x",
    [782907] = "ÆROII",
    [783085] = "Riders-on-the-Storm",
    [783897] = "Royal Marines",
    [786057] = "St Blackwood",
    [786279] = "The Traveling Torchbugs",
    [786309] = "Kynareth's Garden",
    [787197] = "Dirty Secret's",
    [788431] = "Maxwell House Gummibears",
    [788539] = "Second Empire of Cyrodiil",
    [788815] = "Redfur Trading Post",
    [789565] = "Krafty Retro Designs",
    [790227] = "The White Stallions",
    [790581] = "Nubcake",
    [790759] = "SkaaI",
    [793617] = "Tamriel Trade Secrets",
    [794423] = "Alpha Omega Inc",
    [794559] = "Vanguard Gaming",
    [795117] = "House of Marshmellow",
    [795715] = "Dead on the Door",
    [796001] = "akuma",
    [796353] = "Oriental Dragon",
    [797225] = "The Infernal Punks",
    [797635] = "Black Sheep Casuals",
    [798339] = "Magic Muffinz",
    [798717] = "Nahfahlaar's Hoard",
    [799467] = "Pact Battalion",
    [799919] = "Touch My Sweetrolls",
    [800367] = "The Happy Taco",
    [802971] = "Dungeon Pork",
    [804369] = "Section XXII",
    [805985] = "March of Oblivion",
    [807185] = "No Urn left behind",
    [807521] = "Send Us Hate Mail",
    [808943] = "Sweet Trolls",
    [810903] = "Story-Time",
    [812383] = "Banished Souls of Tamriel",
    [812695] = "The Silver Foxes",
    [812817] = "Namiira's Dark Litter",
    [812825] = "Occam's Razors",
    [813039] = "The Trading Union",
    [813755] = "Ghost Bane",
    [814469] = "Magic Muffinz II",
    [815669] = "Grindy Farmers",
    [818373] = "TeaMugs and Axes",
    [819219] = "World Tree Market",
    [819821] = "Order Of The Dead",
    [820309] = "Shadows of Sithis",
    [820651] = "Fractured Void",
    [821861] = "Hearthfire Club",
    [822467] = "Redfur Trading Caravan",
    [822621] = "Definitely A Cult",
    [822773] = "The Sugar Skulls",
    [823269] = "Unicorn Party Central",
    [823333] = "Vincula Doloris",
    [824533] = "The Aurbis Crafting assn",
    [824577] = "The Wandering Wayshrine",
    [825315] = "Righteous Evil",
    [826849] = "Swamp Jelly Sanctuary",
    [826931] = "Bulldogs United",
    [828521] = "Buff My Bank Space",
    [829541] = "Nocturnal Nonsense",
    [829931] = "Fates Lighting",
    [830241] = "Xen of Onslaught",
    [831059] = "The Whiterun Caravan",
    [831963] = "GamingGods",
    [832001] = "Celtic Uprising",
    [834379] = "Lorkhan's Playground",
    [834461] = "Sanguine's Gift",
    [835107] = "Abecean Artisans",
    [835201] = "Aldmeri Emporium Inc",
    [835503] = "Rice Crispy Gang",
    [835809] = "greathouse",
    [836025] = "Nirn Warriors",
    [837571] = "Three Corner Alliance",
    [837575] = "Wandering Stars",
    [839679] = "Viking Uprising",
    [839853] = "First Bank of Aminyon",
    [840633] = "Eclectic Mayhem",
    [841185] = "DC Rebels",
    [842921] = "The Fraternity of Shadows",
    [843933] = "The Systres Factions",
    [843967] = "Fresh Start",
    [845561] = "Kyrre of The North",
    [845813] = "Lizothei's Secret",
    [845945] = "Cauldron Shovel",
    [846331] = "RipTide Crew",
    [846565] = "World Economic For them",
    [846711] = "MassHysteria",
    [847327] = "ShadowRoamers",
    [847853] = "Ultimate Haze",
    [848231] = "Padomaic Sea Pilots",
    [849619] = "The Knights who say AFK",
    [851451] = "Darwin's Adventure",
    [852431] = "The Horns of Tamriel",
    [852509] = "Ultimate gáyman Crew",
    [853265] = "Greatest Of All Time",
    [855241] = "Fresh Start Too",
    [855853] = "GrandExchange",
    [856727] = "Eternal Noobs",
    [860455] = "Oblivion's Covenant",
    [860609] = "MIGHTY COUNCIL",
    [860623] = "The Gummy Guars",
    [860817] = "Humble Services",
    [863737] = "Dead on Crown",
    [864893] = "Scrolls of Mayhem",
    [865105] = "Cadwell's Corner",
    [865409] = "Redfur Dealers",
    [866115] = "Allons y",
    [868209] = "Hyrule Guard",
    [868949] = "Union of Office Workers",
    [869323] = "OneNOnly",
    [869331] = "Oops I Died",
    [869871] = "Allegiance of Asgard",
    [870005] = "Dark Portents",
    [871107] = "Icc Caravan Company",
    [871877] = "Prophets Family",
    [873955] = "Ebony Furnace",
    [873979] = "True Neutral Trading",
    [874133] = "The Salty Mudcrab",
    [874539] = "Sthun's Eyes Guard",
    [874585] = "Skyrim Pirates",
    [874953] = "Blue Road Bank",
    [876357] = "Fond Memory",
    [876403] = "Not a Bot",
    [876787] = "I'kiia Home and Hearth",
    [877719] = "OneNOnly II",
    [877847] = "Tree to Stone Trading Co",
    [879149] = "Lazy Alliance",
    [879937] = "MEATtheBREACH",
    [880387] = "Legion Latina",
    [880633] = "Atlas Retreat",
    [881493] = "Xylophone and Candy",
    [882127] = "ANCIENT DRAGONSLAYERS",
    [882935] = "High Elf Court",
    [882963] = "ChinaLoreLike",
    [883047] = "Red Elites",
    [883387] = "Chaotic Ways",
    [883717] = "Dark Portents Light",
    [883781] = "Darkness of Sanguinaris",
    [885113] = "Midnight Wolf Pack",
    [885277] = "The Obsidian Syndicate",
    [887995] = "Turtle Emporium",
    [889785] = "OneNOnly IIl",
    [889835] = "Pröwl",
    [890619] = "Entirely Social Outcasts",
    [890631] = "Viridian Might",
    [891173] = "Ayrenn's Descendats",
    [891513] = "The Boba Fettuccines",
    [893049] = "Scrivener Supplies",
    [893563] = "Fowl Failing",
    [895159] = "Empress's Eyes",
    [895699] = "Glass Furnace",
    [896191] = "Ayleid Demigods",
    [897729] = "Kings of Dominion",
    [897919] = "The Green Scorpion",
    [898391] = "Templo da Nona Torre",
    [900995] = "Oddball-Exchange",
    [901899] = "Thicc Goth Mommies",
    [902297] = "OneNOnly IV",
    [902797] = "C Co-The Ascended Order",
    [903213] = "Ninja Siegers Inc",
    [903727] = "The Wandering Spirit",
    [903795] = "Khajiit Virtue Academy",
    [906399] = "Nocturnal Citizens",
    [908471] = "East Eastmarch Company",
    [909383] = "Righteous Evil II",
    [909555] = "Golden Sun Tavern",
    [911443] = "AviesAngels",
    [914545] = "School of Vitless Mudfish",
    [916869] = "Dragonbone Furnace",
    [917243] = "Arkadius N Pals",
    [917861] = "Great Gold Giveaway",
    [918783] = "Tamriel Buy and Sell II",
    [922739] = "Anti-Social Trading Guild",
    [924269] = "BlackSoulz",
    [925515] = "The Solo Community Hub",
    [926279] = "We Went Elsweyr",
    [926287] = "OutOfSync",
    [926885] = "Bad Ømens Cult",
    [927611] = "D Co-The Ascended Order",
    [927851] = "Dragon of Wrath",
    [929911] = "Confederación Hispana",
    [931739] = "Beekeeper's",
    [932789] = "Ivory Syndicate",
    [932947] = "Chances Are We're High",
    [933091] = "No Frills Trade Service",
    [935085] = "Malachite Furnace",
    [935303] = "Just the Gear Ma'am",
    [938741] = "The Gilded Trudge",
    [941863] = "Niubis Help Newbies",
    [943693] = "The Knights of Ruin",
    [945565] = "The Housing Commission",
    [945799] = "The Hartian Empire",
    [947841] = "Here to Help",
    [951145] = "The LatchKey Kids",
    [952979] = "Cheesemonger's Tavern",
    [953457] = "Lethal Rejection",
    [953985] = "Fort Redwater Company",
    [954165] = "EP's Rejects",
    [954963] = "Bzerk",
    [958311] = "Nocturnal's Company",
    [958595] = "Forgive and Forget",
    [960355] = "HuckleBerry's Posse",
    [964445] = "Pirate Kittens",
    [965251] = "Ghosts of BlackwaterBlade",
    [965643] = "Keep Kind Exports",
    [967171] = "Salem Senior Center",
    [979277] = "Ancient Exchange",
    [980801] = "ShenZhou VI",
    [994181] = "Dominion Deadbeats",
    [996525] = "Grippy Sock Collective",
    [996681] = "Fade to Oblivion",
    [998389] = "Platinum Pincer",
    [999643] = "The Lunar Fang Accord",
    [1001763] = "Base Camp",
    [1008143] = "Brothers Of The Temple",
    [1016993] = "Margog's Miscreants",
}

-- Fast case-insensitive lookup tables built once on load
FR.KIOSK_BY_LOWER = {}
FR.KIOSK_BY_TRADER_CITY = {}

for traderName, info in pairs(FR.KIOSK_DIRECTORY) do
    local tLower = string.lower(traderName)
    FR.KIOSK_BY_LOWER[tLower] = info
    if info.city and info.city ~= "" then
        FR.KIOSK_BY_TRADER_CITY[tLower .. "@" .. string.lower(info.city)] = info
    end
end

--[[ =========================================================================
     CANONICAL KIOSK RESOLUTION & BANKER QUARANTINE
========================================================================= ]]--

local function CleanEsoString(str)
    if not str or type(str) ~= "string" then return "" end
    -- Strip ESO gender/grammar control codes (^M, ^F, ^n, etc.) and trim
    local cleaned = str:gsub("%^[A-Za-z]+", ""):gsub("^%s+", ""):gsub("%s+$", "")
    return cleaned
end

function FR:ResolveCanonicalKiosk(rawTrader, rawCity, rawZone)
    local cleanTrader = CleanEsoString(rawTrader)
    if cleanTrader == "" then return nil end

    local parsedCity = CleanEsoString(rawCity)
    local parsedZone = CleanEsoString(rawZone)

    if parsedZone == "Owned Guild Stall" or parsedZone == "Guild Finder" or parsedZone == "Tamriel" then
        parsedZone = ""
    end
    if parsedCity == "Guild Ledger" or parsedCity == "Guild Finder" or parsedCity == "Tamriel" then
        parsedCity = ""
    end

    -- Split "<Trader> in <City>" (EN/DE) or "<Trader> à <City>" (FR)
    local tPart, cPart = string.match(cleanTrader, "^(.-)%s+in%s+(.+)$")
    if not tPart then
        tPart, cPart = string.match(cleanTrader, "^(.-)%s+\195\160%s+(.+)$")
    end
    if tPart and tPart ~= "" then
        cleanTrader = CleanEsoString(tPart)
        if parsedCity == "" and cPart then
            parsedCity = CleanEsoString(cPart)
        end
    end

    local lowerTrader = string.lower(cleanTrader)
    if self.IRREGULAR_TRADERS[lowerTrader] then
        cleanTrader = self.IRREGULAR_TRADERS[lowerTrader]
        lowerTrader = string.lower(cleanTrader)
    end

    -- 1. Exact Trader + City match
    if parsedCity ~= "" then
        local tcHit = self.KIOSK_BY_TRADER_CITY[lowerTrader .. "@" .. string.lower(parsedCity)]
        if tcHit then
            return {
                trader = tcHit.trader,
                city = tcHit.city,
                zone = tcHit.zone,
                isOutlaw = tcHit.isOutlaw,
                matchedDirectory = true,
            }
        end
    end

    -- 2. Exact Trader match in 291 Directory
    local exactHit = self.KIOSK_BY_LOWER[lowerTrader]
    if exactHit then
        return {
            trader = exactHit.trader,
            city = exactHit.city,
            zone = exactHit.zone,
            isOutlaw = exactHit.isOutlaw,
            matchedDirectory = true,
        }
    end

    return {
        trader = cleanTrader,
        city = parsedCity ~= "" and parsedCity or "Tamriel",
        zone = parsedZone ~= "" and parsedZone or "Tamriel",
        isOutlaw = false,
        matchedDirectory = false,
    }
end

function FR:NormalizeKioskGuildName(rawGuildName, guildId, sourceTag)
    local cleaned = CleanEsoString(rawGuildName)
    local numGuildId = tonumber(guildId) or 0
    local src = tostring(sourceTag or "")

    -- Positive guildId or guild-attribute source guarantees an active owning guild (e.g., guild 536021 "Available Trader Kiosk")
    if numGuildId > 0 or src == "Guild Finder Sweep" or src == "Guild ID Probe" or src == "Owned Guild Stall" or src == "Owned Ledger" then
        if cleaned == "" or string.lower(cleaned) == "vacant" then
            if numGuildId > 0 and self.SEED_TRADING_GUILDS and self.SEED_TRADING_GUILDS[numGuildId] then
                return self.SEED_TRADING_GUILDS[numGuildId]
            end
            return "Available Trader Kiosk"
        end
        return cleaned
    end

    if cleaned == "" then return "Vacant" end
    local lower = string.lower(cleaned)
    local defaultAttr = GetString and GetString(SI_GUILD_FINDER_GUILD_INFO_DEFAULT_ATTRIBUTE_VALUE) or ""
    local noOwnerCaption = GetString and GetString(SI_GUILD_KIOSK_DISPLAY_CAPTION_WITHOUT_OWNER) or "Guild Trader"

    if lower == "none"
        or lower == "vacant"
        or lower == "unclaimed"
        or lower == "--"
        or lower == "no hiring guild (vacant)"
        or lower == "guild trader"
        or (defaultAttr ~= "" and cleaned == defaultAttr)
        or (noOwnerCaption ~= "" and cleaned == noOwnerCaption)
    then
        return "Vacant"
    end
    return cleaned
end

function FR:RecordCanonicalKiosk(rawTrader, guildId, rawGuildName, sourceTag, xCoord, yCoord, silent)
    if not self.savedVars or not self.savedVars.kiosks then return false end
    if self.isBumping then return false end

    local resolved = self:ResolveCanonicalKiosk(rawTrader)
    -- Strict Directory Invariant: Only record verified Tamriel Guild Traders from the 291 directory.
    -- This permanently blocks all Banker NPCs (Kargiz, Slera Cinna, Angier Stower, assistants, etc.).
    if not resolved or not resolved.matchedDirectory then
        return false
    end

    if self:IsBlacklistedTrader(resolved.trader) then
        return false
    end

    local guildName = self:NormalizeKioskGuildName(rawGuildName, guildId, sourceTag)
    local cleanGuildId = (guildName == "Vacant") and 0 or (tonumber(guildId) or 0)
    if cleanGuildId == 0 and string.lower(guildName) == "available trader kiosk" then
        cleanGuildId = 536021
    end

    -- Update KnownTradingGuilds registry whenever a valid guildId is observed
    if cleanGuildId > 0 and guildName ~= "Vacant" then
        if not self.savedVars.knownTradingGuilds then
            self.savedVars.knownTradingGuilds = {}
        end
        self.savedVars.knownTradingGuilds[cleanGuildId] = guildName
    end

    -- Clean up any legacy "<Trader> in <City>" un-normalized key in savedVars.kiosks
    if rawTrader and rawTrader ~= resolved.trader and self.savedVars.kiosks[rawTrader] then
        self.savedVars.kiosks[rawTrader] = nil
    end

    local prevObs = self.savedVars.kiosks[resolved.trader]
    local nowTs = GetTimeStamp()
    local curWeekStart = self.GetTuesdayTradeWeek and self:GetTuesdayTradeWeek(nowTs) or 0
    local isNewOrChanged = not prevObs or (prevObs.guildName ~= guildName)

    if prevObs and not isNewOrChanged then
        local prevTs = tonumber(prevObs.timestamp) or 0
        if prevTs >= curWeekStart
            and prevObs.x and prevObs.x ~= "0.0000"
            and prevObs.y and prevObs.y ~= "0.0000"
            and (nowTs - prevTs) < 60
        then
            return true, false
        end
    end

    -- Preserve player coordinates if already captured in-person and new observation is from Guild Finder
    local xStr = xCoord and string.format("%.4f", tonumber(xCoord) or 0) or (prevObs and prevObs.x) or "0.0000"
    local yStr = yCoord and string.format("%.4f", tonumber(yCoord) or 0) or (prevObs and prevObs.y) or "0.0000"

    self.savedVars.kiosks[resolved.trader] = {
        trader = resolved.trader,
        guildId = cleanGuildId,
        guildName = guildName,
        zone = resolved.zone,
        city = resolved.city,
        x = xStr,
        y = yStr,
        timestamp = nowTs,
        observedBy = GetDisplayName(),
        source = sourceTag or "Scouted",
    }

    -- Single-Kiosk-Per-Guild Invariant in savedVars.kiosks for the active trade week:
    -- If this guild (non-Vacant, non-placeholder) was recorded on a DIFFERENT trader during the current trade week,
    -- prune the conflicting current-week trader entry while preserving prior-week history.
    if guildName ~= "Vacant"
        and guildName ~= "Available Trader Kiosk"
        and cleanGuildId ~= 536021
        and curWeekStart > 0
    then
        for otherTrader, otherObs in pairs(self.savedVars.kiosks) do
            if otherTrader ~= resolved.trader and otherObs and otherObs.guildName == guildName then
                if (tonumber(otherObs.timestamp) or 0) >= curWeekStart then
                    self.savedVars.kiosks[otherTrader] = nil
                end
            end
        end
    end

    if not silent and isNewOrChanged and self.savedVars.settings and self.savedVars.settings.announceKioskRecon then
        local badgeColor = (guildName == "Vacant") and "FFD700" or "00FF00"
        self.PrintChat(string.format("Kiosk Recon [%s]: %s in %s (%s) -> %s",
            sourceTag or "Recon",
            self.ColorText(resolved.trader, "FFFFFF"),
            self.ColorText(resolved.city, "00FFCC"),
            resolved.zone,
            self.ColorText(guildName, badgeColor)))
    end

    return true, isNewOrChanged
end

--[[ =========================================================================
     PASSIVE RETICLE, CHATTER & TRADING HOUSE HARVESTER
========================================================================= ]]--

local _captionOwnerPattern = nil
local function ExtractGuildNameFromCaption(caption)
    if not caption or caption == "" then return nil end
    if not _captionOwnerPattern and GetString and SI_GUILD_KIOSK_DISPLAY_CAPTION_WITH_OWNER then
        local template = GetString(SI_GUILD_KIOSK_DISPLAY_CAPTION_WITH_OWNER)
        if template and template ~= "" then
            _captionOwnerPattern = template:gsub("%(<<1>>%)", "%%((.+)%%)")
        end
    end
    if _captionOwnerPattern then
        local m = string.match(caption, _captionOwnerPattern)
        if m and m ~= "" then return CleanEsoString(m) end
    end
    -- Fallback: extract text inside parentheses if present, else raw caption
    local paren = string.match(caption, "%((.+)%)")
    if paren and paren ~= "" then return CleanEsoString(paren) end
    return CleanEsoString(caption)
end

function FR:InspectUnitKiosk(unitTag, sourceTag, silent)
    if not unitTag or not DoesUnitExist or not DoesUnitExist(unitTag) then return false end
    if not IsUnitGuildKiosk or not IsUnitGuildKiosk(unitTag) then return false end

    local unitName = GetUnitName(unitTag)
    if not unitName or unitName == "" then return false end

    local ownerGuildId = GetUnitGuildKioskOwner and GetUnitGuildKioskOwner(unitTag) or nil
    local caption = GetUnitCaption and GetUnitCaption(unitTag) or ""
    local noOwnerCaption = GetString and GetString(SI_GUILD_KIOSK_DISPLAY_CAPTION_WITHOUT_OWNER) or "Guild Trader"

    local guildName = nil
    local guildId = 0

    if ownerGuildId == 0 then
        -- Verified Vacant Kiosk!
        if caption == "" or caption == noOwnerCaption then
            guildName = "Vacant"
            guildId = 0
        end
    elseif ownerGuildId and ownerGuildId > 0 then
        guildId = ownerGuildId
        local extracted = ExtractGuildNameFromCaption(caption)
        if extracted and extracted ~= "" and extracted ~= noOwnerCaption then
            guildName = extracted
        elseif self.savedVars and self.savedVars.knownTradingGuilds and self.savedVars.knownTradingGuilds[guildId] then
            guildName = self.savedVars.knownTradingGuilds[guildId]
        end
    end

    if not guildName then return false end

    local x, y = GetMapPlayerPosition("player")
    return self:RecordCanonicalKiosk(unitName, guildId, guildName, sourceTag or "Reticle Recon", x, y, silent)
end

--[[ =========================================================================
     ACTIVE WEEKLY GUILD BIDS HARVESTER
========================================================================= ]]--

function FR:PollActiveGuildBids()
    if not RequestGuildKioskActiveBids or not GetNumGuilds then return 0 end
    if self.kioskSweepState and self.kioskSweepState.active then return 0 end
    local now = GetTimeStamp()
    if self._lastActiveBidsPollTs and (now - self._lastActiveBidsPollTs) < 300 then
        return 0
    end
    self._lastActiveBidsPollTs = now

    local requested = 0
    for i = 1, GetNumGuilds() do
        local guildId = GetGuildId(i)
        if guildId and guildId > 0 then
            if not DoesPlayerHaveGuildPermission or DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_GUILD_KIOSK_BID) then
                requested = requested + 1
                local delayMs = (requested - 1) * 3500
                zo_callLater(function()
                    if not (self.kioskSweepState and self.kioskSweepState.active) then
                        pcall(RequestGuildKioskActiveBids, guildId)
                    end
                end, delayMs)
            end
        end
    end
    return requested
end

function FR:OnActiveBidsResponse(guildId, result)
    if not guildId or not GetNumGuildKioskActiveBids or not GetGuildKioskActiveBidInfo then return end
    local numBids = GetNumGuildKioskActiveBids(guildId) or 0
    if numBids <= 0 then return end

    if not self.savedVars or not self.savedVars.staff then return end
    if not self.savedVars.staff.bids then self.savedVars.staff.bids = {} end

    local guildName = GetGuildName(guildId) or "Guild"
    local now = GetTimeStamp()
    local added = 0

    for idx = 1, numBids do
        local timeSinceBidS, bidAmount, kioskName, bidderDisplayName = GetGuildKioskActiveBidInfo(guildId, idx)
        if bidAmount and bidAmount > 0 and kioskName and kioskName ~= "" then
            local bidTs = now - (tonumber(timeSinceBidS) or 0)
            local cleanBidder = CleanEsoString(bidderDisplayName or "Officer")
            -- Check if an equivalent bid already exists in savedVars.staff.bids within 120s
            local exists = false
            for _, existing in pairs(self.savedVars.staff.bids) do
                if existing.guildId == guildId
                    and tonumber(existing.amount) == tonumber(bidAmount)
                    and existing.kioskName == kioskName
                    and math.abs((tonumber(existing.timestamp) or 0) - bidTs) < 300
                then
                    exists = true
                    break
                end
            end

            if not exists then
                local synthId = string.format("active_%d_%d_%d", guildId, bidAmount, idx)
                self.savedVars.staff.bids[synthId] = {
                    id = synthId,
                    timestamp = bidTs,
                    guildId = guildId,
                    guildName = guildName,
                    bidder = cleanBidder,
                    kioskName = kioskName,
                    amount = bidAmount,
                    status = "Pending",
                }
                added = added + 1
            end
        end
    end

    if added > 0 and self.UpdateBidsUI and self.consoleWindow and not self.consoleWindow:IsHidden() then
        self:UpdateBidsUI()
    end
end

--[[ =========================================================================
     ENGINE 1 & 2: AUTOMATED GUILD FINDER SWEEP + KNOWN GUILD ID PROBER
========================================================================= ]]--

FR.kioskSweepState = {
    active = false,
    isSilent = false,
    phase = "idle", -- "idle", "guild_finder", "guild_probe"
    passIndex = 0,
    totalPasses = 0,
    currentPassLabel = "",
    probeIndex = 0,
    totalProbes = 0,
    currentGuildId = nil,
    currentGuildName = "",
    kiosksHarvested = 0,
    newOrUpdated = 0,
    startedAt = 0,
    lastCompletedTs = 0,
    lastHarvested = 0,
    lastNewOrUpdated = 0,
    seenGuildIds = {},
    probeQueue = {},
    pendingGuildId = nil,
    probeRetries = 0,
}

local function BuildGuildFinderPasses()
    local passes = {}
    local tradingFocus = GUILD_FOCUS_ATTRIBUTE_VALUE_TRADING or 1
    local alliances = {
        { id = ALLIANCE_ALDMERI_DOMINION or 1, label = "Trading (AD)" },
        { id = ALLIANCE_DAGGERFALL_COVENANT or 3, label = "Trading (DC)" },
        { id = ALLIANCE_EBONHEART_PACT or 2, label = "Trading (EP)" },
    }
    for _, a in ipairs(alliances) do
        table.insert(passes, {
            focus = tradingFocus,
            alliance = a.id,
            label = a.label,
        })
    end

    local otherFocuses = {
        { id = GUILD_FOCUS_ATTRIBUTE_VALUE_SOCIAL, label = "Social" },
        { id = GUILD_FOCUS_ATTRIBUTE_VALUE_GROUP_PVE, label = "Group PvE" },
        { id = GUILD_FOCUS_ATTRIBUTE_VALUE_CRAFTING, label = "Crafting" },
        { id = GUILD_FOCUS_ATTRIBUTE_VALUE_PVP, label = "PvP" },
        { id = GUILD_FOCUS_ATTRIBUTE_VALUE_QUESTING, label = "Questing" },
        { id = GUILD_FOCUS_ATTRIBUTE_VALUE_ROLEPLAYING, label = "Roleplaying" },
    }
    for _, f in ipairs(otherFocuses) do
        if f.id then
            table.insert(passes, {
                focus = f.id,
                alliance = nil, -- all alliances
                label = f.label,
            })
        end
    end
    return passes
end

local function EnableAllGuildFinderLanguages()
    if not SetGuildFinderLanguageFilterValue then return end
    local b = GUILD_LANGUAGE_ATTRIBUTE_VALUE_ITERATION_BEGIN or 1
    local e = GUILD_LANGUAGE_ATTRIBUTE_VALUE_ITERATION_END or 6
    for lang = b, e do
        SetGuildFinderLanguageFilterValue(lang, true)
    end
    if GUILD_LANGUAGE_ATTRIBUTE_VALUE_OTHER then
        pcall(SetGuildFinderLanguageFilterValue, GUILD_LANGUAGE_ATTRIBUTE_VALUE_OTHER, true)
    else
        pcall(SetGuildFinderLanguageFilterValue, 0, true)
    end
end

local function SetGuildFinderAlliances(onlyAlliance)
    if not SetGuildFinderAllianceFilterValue then return end
    local all = {
        ALLIANCE_ALDMERI_DOMINION or 1,
        ALLIANCE_EBONHEART_PACT or 2,
        ALLIANCE_DAGGERFALL_COVENANT or 3,
    }
    for _, a in ipairs(all) do
        SetGuildFinderAllianceFilterValue(a, (onlyAlliance == nil) or (a == onlyAlliance))
    end
end

function FR:UpdateSweepButtonUI()
    local st = self.kioskSweepState
    if self.sweepTamrielBtn then
        if st and st.active then
            if st.phase == "guild_finder" then
                self.sweepTamrielBtn:SetText(string.format("Sweep %d/%d (%d)", st.passIndex, st.totalPasses, st.kiosksHarvested))
            elseif st.phase == "guild_probe" then
                self.sweepTamrielBtn:SetText(string.format("Probe %d/%d (%d)", st.probeIndex, st.totalProbes, st.kiosksHarvested))
            else
                self.sweepTamrielBtn:SetText("Sweeping...")
            end
        else
            self.sweepTamrielBtn:SetText("Sweep Tamriel Kiosks")
        end
    end
    if self.UpdateHUD then
        self:UpdateHUD()
    end
end

function FR:HarvestGuildAttributeKiosk(guildId, sourceTag)
    if not guildId or guildId <= 0 then return false end
    local guildName = GetGuildNameAttribute and GetGuildNameAttribute(guildId) or nil
    if (not guildName or guildName == "") and self.savedVars and self.savedVars.knownTradingGuilds then
        guildName = self.savedVars.knownTradingGuilds[guildId]
    end
    if not guildName or guildName == "" then return false end

    -- Always record the guild name in knownTradingGuilds if valid
    if self.savedVars and self.savedVars.knownTradingGuilds then
        self.savedVars.knownTradingGuilds[guildId] = CleanEsoString(guildName)
    end

    local kioskInfo = GetGuildKioskAttribute and GetGuildKioskAttribute(guildId) or nil
    local defaultAttr = GetString and GetString(SI_GUILD_FINDER_GUILD_INFO_DEFAULT_ATTRIBUTE_VALUE) or ""
    if not kioskInfo or kioskInfo == "" or (defaultAttr ~= "" and kioskInfo == defaultAttr) then
        return false
    end

    local ok, changed = self:RecordCanonicalKiosk(kioskInfo, guildId, guildName, sourceTag or "Guild Finder", nil, nil, true)
    if ok then
        local st = self.kioskSweepState
        if st and st.active then
            st.kiosksHarvested = (st.kiosksHarvested or 0) + 1
            if changed then
                st.newOrUpdated = (st.newOrUpdated or 0) + 1
            end
        end
    end
    return ok
end

function FR:StopKioskSweep(reason)
    local st = self.kioskSweepState
    if not st or not st.active then return end
    st.active = false
    st.phase = "idle"
    st.pendingGuildId = nil
    st.currentGuildId = nil
    st.currentGuildName = ""
    st.lastCompletedTs = GetTimeStamp()
    st.lastHarvested = st.kiosksHarvested or 0
    st.lastNewOrUpdated = st.newOrUpdated or 0

    if ResetGuildFinderFilters then
        pcall(ResetGuildFinderFilters)
    end

    self:UpdateSweepButtonUI()
    if self.UpdateBidsUI and self.consoleWindow and not self.consoleWindow:IsHidden() then
        self:UpdateBidsUI()
    end

    local totalWeekKiosks = self:GetCurrentWeekKioskCount()
    self.PrintChat(string.format("Tamriel Kiosk Sweep %s: %s kiosks harvested (%s new/updated). Current week coverage: %s/291 Tamriel traders.",
        reason or "complete",
        self.ColorText(tostring(st.kiosksHarvested or 0), "00FFCC"),
        self.ColorText(tostring(st.newOrUpdated or 0), "59E08A"),
        self.ColorText(tostring(totalWeekKiosks), "FFD700")))
    if self.PlayFissalSound then self.PlayFissalSound() end
end

function FR:GetCurrentWeekKioskCount()
    if not self.savedVars or not self.savedVars.kiosks then return 0 end
    local curWeekStart = 0
    if self.GetTuesdayTradeWeek then
        curWeekStart = self:GetTuesdayTradeWeek(GetTimeStamp())
    end
    local count = 0
    for _, k in pairs(self.savedVars.kiosks) do
        if (tonumber(k.timestamp) or 0) >= curWeekStart then
            count = count + 1
        end
    end
    return count
end

function FR:GetKioskReconTelemetry()
    local st = self.kioskSweepState or {}
    local curWeekStart = self.GetTuesdayTradeWeek and self:GetTuesdayTradeWeek(GetTimeStamp()) or 0
    local weekCount = 0
    local totalCataloged = 0
    local bySource = {
        guildFinder = 0,
        guildProbe = 0,
        owned = 0,
        scouted = 0,
        vacant = 0,
    }

    if self.savedVars and self.savedVars.kiosks then
        for _, k in pairs(self.savedVars.kiosks) do
            totalCataloged = totalCataloged + 1
            if (tonumber(k.timestamp) or 0) >= curWeekStart then
                weekCount = weekCount + 1
                local gLower = string.lower(k.guildName or "")
                local src = k.source or ""
                local gId = tonumber(k.guildId) or 0
                if gId == 0
                    and src ~= "Guild Finder Sweep"
                    and src ~= "Guild ID Probe"
                    and src ~= "Owned Guild Stall"
                    and (gLower == "vacant" or gLower == "none (open spot)" or gLower == "open spot" or gLower == "unclaimed")
                then
                    bySource.vacant = bySource.vacant + 1
                end
                if string.find(src, "Guild Finder") then
                    bySource.guildFinder = bySource.guildFinder + 1
                elseif string.find(src, "Probe") then
                    bySource.guildProbe = bySource.guildProbe + 1
                elseif string.find(src, "Owned") then
                    bySource.owned = bySource.owned + 1
                else
                    bySource.scouted = bySource.scouted + 1
                end
            end
        end
    end

    local etaSeconds = 0
    if st.active then
        if st.phase == "guild_finder" then
            local remPasses = math.max(0, (st.totalPasses or 9) - (st.passIndex or 0))
            etaSeconds = math.ceil(remPasses * 3.5)
        elseif st.phase == "guild_probe" then
            local remProbes = math.max(0, (st.totalProbes or 0) - (st.probeIndex or 0))
            etaSeconds = math.ceil(remProbes * 4.2)
        end
    end

    local totalKnownGuilds = NonContiguousCount((self.savedVars and self.savedVars.knownTradingGuilds) or {})
    local probedThisWeek = 0
    if self.savedVars and self.savedVars.probedGuildsWeek and self.savedVars.probedGuildsWeek.weekStart == curWeekStart and self.savedVars.probedGuildsWeek.probed then
        probedThisWeek = NonContiguousCount(self.savedVars.probedGuildsWeek.probed)
    end

    return {
        active = st.active == true,
        phase = st.phase or "idle",
        passIndex = st.passIndex or 0,
        totalPasses = st.totalPasses or 0,
        currentPassLabel = st.currentPassLabel or "",
        probeIndex = st.probeIndex or 0,
        totalProbes = st.totalProbes or 0,
        currentGuildId = st.currentGuildId,
        currentGuildName = st.currentGuildName or "",
        kiosksHarvested = st.kiosksHarvested or 0,
        newOrUpdated = st.newOrUpdated or 0,
        lastCompletedTs = st.lastCompletedTs or 0,
        lastHarvested = st.lastHarvested or 0,
        lastNewOrUpdated = st.lastNewOrUpdated or 0,
        etaSeconds = etaSeconds,
        weekCount = weekCount,
        totalDirectory = 291,
        totalCataloged = totalCataloged,
        totalKnownGuilds = totalKnownGuilds,
        probedThisWeek = probedThisWeek,
        bySource = bySource,
    }
end

local function IsGuildBrowserShowing()
    if not SCENE_MANAGER or not SCENE_MANAGER.IsShowing then return false end
    return SCENE_MANAGER:IsShowing("guildBrowserKeyboard") or SCENE_MANAGER:IsShowing("guildBrowserGamepad")
end

function FR:ExecuteNextGuildFinderPass()
    local st = self.kioskSweepState
    if not st or not st.active or st.phase ~= "guild_finder" then return end

    if IsUnitInCombat and IsUnitInCombat("player") then
        zo_callLater(function()
            self:ExecuteNextGuildFinderPass()
        end, 4000)
        return
    end

    if self.autoRankBatchActive or self.isAuditBatchRunning then
        zo_callLater(function()
            self:ExecuteNextGuildFinderPass()
        end, 5000)
        return
    end

    if st.isSilent and IsGuildBrowserShowing() then
        zo_callLater(function()
            self:ExecuteNextGuildFinderPass()
        end, 5000)
        return
    end

    if st.passIndex >= st.totalPasses then
        -- Phase 1 complete! Give ESO's server message bucket a polite 4.5s breather before Phase 2
        st.currentPassLabel = "Cooling pipe..."
        self:UpdateSweepButtonUI()
        zo_callLater(function()
            if st.active then
                self:StartKnownGuildProbe()
            end
        end, 4500)
        return
    end

    if GuildFinderIsSearchOnCooldown and GuildFinderIsSearchOnCooldown() then
        zo_callLater(function()
            self:ExecuteNextGuildFinderPass()
        end, 2500)
        return
    end

    st.passIndex = st.passIndex + 1
    local pass = st.passes[st.passIndex]
    st.currentPassLabel = pass and pass.label or ""
    self:UpdateSweepButtonUI()

    if SetGuildFinderHasTradersSearchFilter then
        SetGuildFinderHasTradersSearchFilter(true)
    end
    EnableAllGuildFinderLanguages()
    SetGuildFinderAlliances(pass.alliance)
    if SetGuildFinderFocusSearchFilter and pass.focus then
        SetGuildFinderFocusSearchFilter(pass.focus)
    end

    if GuildFinderRequestSearch then
        st.activeSearchId = GuildFinderRequestSearch()
        local expectedPass = st.passIndex
        -- Safety timeout in case ESO drops a search packet
        zo_callLater(function()
            if st.active and st.phase == "guild_finder" and st.passIndex == expectedPass then
                self:ExecuteNextGuildFinderPass()
            end
        end, 7500)
    else
        self:StartKnownGuildProbe()
    end
end

function FR:OnGuildFinderSearchComplete(eventCode, searchId)
    local st = self.kioskSweepState
    local numResults = GuildFinderGetNumSearchResults and GuildFinderGetNumSearchResults() or 0

    -- Even if the user manually searched the Guild Finder outside of an active sweep,
    -- passively harvest any guild trader attributes returned by the client!
    for i = 1, numResults do
        local guildId = GuildFinderGetSearchResultGuildId and GuildFinderGetSearchResultGuildId(i)
        if guildId and guildId > 0 then
            if st and st.active then
                st.seenGuildIds[guildId] = true
            end
            self:HarvestGuildAttributeKiosk(guildId, "Guild Finder Sweep")
        end
    end

    if not st or not st.active or st.phase ~= "guild_finder" then
        if self.UpdateHUD then self:UpdateHUD() end
        return
    end

    self:UpdateSweepButtonUI()
    if self.UpdateBidsUI and self.consoleWindow and not self.consoleWindow:IsHidden() and self.bidsSubView == "recon" then
        self:UpdateBidsUI()
    end

    -- Polite 3.2s spacing between Guild Finder search packets to prevent ESO server flood kicks
    zo_callLater(function()
        self:ExecuteNextGuildFinderPass()
    end, 3200)
end

function FR:EnsureProbedGuildsWeekTable()
    local curWeekStart = self.GetTuesdayTradeWeek and self:GetTuesdayTradeWeek(GetTimeStamp()) or 0
    if not self.savedVars.probedGuildsWeek or self.savedVars.probedGuildsWeek.weekStart ~= curWeekStart then
        self.savedVars.probedGuildsWeek = {
            weekStart = curWeekStart,
            probed = {},
        }
    end
    if not self.savedVars.probedGuildsWeek.probed then
        self.savedVars.probedGuildsWeek.probed = {}
    end
    return self.savedVars.probedGuildsWeek.probed, curWeekStart
end

-- Maximum uncached C++ Guild Info network requests allowed per single sweep to guarantee zero server disconnects
local MAX_NETWORK_PROBES_PER_SWEEP = 18

function FR:StartKnownGuildProbe()
    local st = self.kioskSweepState
    if not st or not st.active then return end

    st.phase = "guild_probe"
    st.probeQueue = {}
    st.probeIndex = 0
    st.networkProbesSent = 0
    st.probeRetries = 0

    local probedMap, curWeekStart = self:EnsureProbedGuildsWeekTable()
    local alreadyConfirmedGuildIds = {}
    local priorKioskHolders = {}
    if self.savedVars and self.savedVars.kiosks then
        for _, k in pairs(self.savedVars.kiosks) do
            local gId = tonumber(k.guildId) or 0
            if gId > 0 then
                if (tonumber(k.timestamp) or 0) >= curWeekStart then
                    alreadyConfirmedGuildIds[gId] = true
                else
                    priorKioskHolders[gId] = true
                end
            end
        end
    end

    local knownMap = (self.savedVars and self.savedVars.knownTradingGuilds) or {}
    local cachedInMemoryList = {}
    local priorityUnprobedList = {}
    local standardUnprobedList = {}

    for gId, _ in pairs(knownMap) do
        local numId = tonumber(gId)
        if numId and numId > 0 and not st.seenGuildIds[numId] and not alreadyConfirmedGuildIds[numId] then
            local alreadyCached = false
            if DoesGuildDataHaveInitializedAttributes and GUILD_META_DATA_ATTRIBUTE_KIOSK then
                alreadyCached = DoesGuildDataHaveInitializedAttributes(numId, GUILD_META_DATA_ATTRIBUTE_KIOSK)
            end
            if alreadyCached then
                table.insert(cachedInMemoryList, numId)
            elseif not probedMap[numId] then
                if priorKioskHolders[numId] then
                    table.insert(priorityUnprobedList, numId)
                else
                    table.insert(standardUnprobedList, numId)
                end
            end
        end
    end

    table.sort(cachedInMemoryList)
    table.sort(priorityUnprobedList)
    table.sort(standardUnprobedList)

    -- 1. Harvest all zero-cost client-memory cached guilds immediately
    for _, id in ipairs(cachedInMemoryList) do
        probedMap[id] = GetTimeStamp()
        self:HarvestGuildAttributeKiosk(id, "Guild ID Probe")
    end

    -- 2. Enqueue up to MAX_NETWORK_PROBES_PER_SWEEP uncached guilds (prioritizing prior kiosk holders)
    for _, id in ipairs(priorityUnprobedList) do
        if #st.probeQueue < MAX_NETWORK_PROBES_PER_SWEEP then
            table.insert(st.probeQueue, id)
        end
    end
    for _, id in ipairs(standardUnprobedList) do
        if #st.probeQueue < MAX_NETWORK_PROBES_PER_SWEEP then
            table.insert(st.probeQueue, id)
        end
    end

    st.totalProbes = #st.probeQueue
    local totalRemainingUnprobed = math.max(0, (#priorityUnprobedList + #standardUnprobedList) - st.totalProbes)
    self:UpdateSweepButtonUI()

    if not st.isSilent and st.totalPasses > 0 then
        if st.totalProbes > 0 then
            self.PrintChat(string.format("Sweep Phase 1/2 (Guild Finder) complete: %s kiosks mapped. Phase 2/2: Politely probing batch of %s unlisted guilds (~3.6s pacing, %s remaining for later sweeps)...",
                self.ColorText(tostring(st.kiosksHarvested or 0), "00FFCC"),
                self.ColorText(tostring(st.totalProbes), "FFD700"),
                tostring(totalRemainingUnprobed)))
        else
            self.PrintChat(string.format("Sweep Phase 1/2 (Guild Finder) complete: %s kiosks mapped. All known trading guilds already probed for this week!",
                self.ColorText(tostring(st.kiosksHarvested or 0), "00FFCC")))
        end
    end

    if st.totalProbes == 0 then
        self:StopKioskSweep("complete")
        return
    end

    self:ExecuteNextGuildProbe()
end

function FR:ExecuteNextGuildProbe()
    local st = self.kioskSweepState
    if not st or not st.active or st.phase ~= "guild_probe" then return end

    if IsUnitInCombat and IsUnitInCombat("player") then
        zo_callLater(function()
            self:ExecuteNextGuildProbe()
        end, 4000)
        return
    end

    if self.autoRankBatchActive or self.isAuditBatchRunning then
        zo_callLater(function()
            self:ExecuteNextGuildProbe()
        end, 5000)
        return
    end

    if st.isSilent and IsGuildBrowserShowing() then
        zo_callLater(function()
            self:ExecuteNextGuildProbe()
        end, 5000)
        return
    end

    local probedMap = self:EnsureProbedGuildsWeekTable()

    if st.probeIndex >= st.totalProbes then
        self:StopKioskSweep("complete")
        return
    end

    st.probeIndex = st.probeIndex + 1
    local guildId = st.probeQueue[st.probeIndex]
    st.currentGuildId = guildId
    st.currentGuildName = (self.savedVars and self.savedVars.knownTradingGuilds and self.savedVars.knownTradingGuilds[guildId]) or ("Guild #" .. tostring(guildId))

    local alreadyCached = false
    if DoesGuildDataHaveInitializedAttributes and GUILD_META_DATA_ATTRIBUTE_KIOSK then
        alreadyCached = DoesGuildDataHaveInitializedAttributes(guildId, GUILD_META_DATA_ATTRIBUTE_KIOSK)
    end

    if alreadyCached then
        probedMap[guildId] = GetTimeStamp()
        self:HarvestGuildAttributeKiosk(guildId, "Guild ID Probe")
        st.probeRetries = 0
        self:UpdateSweepButtonUI()
        zo_callLater(function()
            self:ExecuteNextGuildProbe()
        end, 250)
        return
    end

    st.pendingGuildId = guildId
    self:UpdateSweepButtonUI()

    local requested = false
    if RequestGuildFinderAttributesForGuild then
        requested = RequestGuildFinderAttributesForGuild(guildId)
    elseif GUILD_BROWSER_MANAGER and GUILD_BROWSER_MANAGER.RequestGuildData then
        requested = GUILD_BROWSER_MANAGER:RequestGuildData(guildId)
    end

    if not requested then
        -- C++ Guild Info pipe signalled cooldown; never flood-retry! Back off 5.0s once, or pause sweep cleanly.
        if (st.probeRetries or 0) < 1 then
            st.probeRetries = (st.probeRetries or 0) + 1
            st.probeIndex = st.probeIndex - 1
            st.pendingGuildId = nil
            zo_callLater(function()
                if st.active and st.phase == "guild_probe" then
                    self:ExecuteNextGuildProbe()
                end
            end, 5000)
            return
        else
            st.pendingGuildId = nil
            self:StopKioskSweep("complete (polite server rate-limit guard)")
            return
        end
    else
        st.probeRetries = 0
        st.networkProbesSent = (st.networkProbesSent or 0) + 1
        -- Wait for EVENT_GUILD_INFO_REQUEST_COMPLETE or timeout after 3.8s (unlisted/deleted guild)
        local expectedIdx = st.probeIndex
        zo_callLater(function()
            if st.active and st.phase == "guild_probe" and st.probeIndex == expectedIdx then
                probedMap[guildId] = GetTimeStamp()
                st.pendingGuildId = nil
                local breatherMs = (st.networkProbesSent % 6 == 0) and 6000 or 3600
                zo_callLater(function()
                    self:ExecuteNextGuildProbe()
                end, breatherMs)
            end
        end, 3800)
        return
    end
end

function FR:OnGuildInfoRequestComplete(eventCode, guildId)
    if not guildId or guildId <= 0 then return end

    local probedMap = self:EnsureProbedGuildsWeekTable()
    probedMap[guildId] = GetTimeStamp()

    -- Always harvest passively even when the user clicks a guild in Guild Browser manually
    self:HarvestGuildAttributeKiosk(guildId, "Guild ID Probe")

    local st = self.kioskSweepState
    if not st or not st.active or st.phase ~= "guild_probe" then
        if self.UpdateHUD then self:UpdateHUD() end
        return
    end

    if st.pendingGuildId == guildId then
        st.pendingGuildId = nil
        self:UpdateSweepButtonUI()
        if st.probeIndex % 6 == 0 and self.UpdateBidsUI and self.consoleWindow and not self.consoleWindow:IsHidden() and self.bidsSubView == "recon" then
            self:UpdateBidsUI()
        end
        -- Polite 3.6s spacing between Guild Info queries (+ 6.0s breather every 6 queries)
        local nextDelayMs = ((st.networkProbesSent or 0) % 6 == 0) and 6000 or 3600
        zo_callLater(function()
            self:ExecuteNextGuildProbe()
        end, nextDelayMs)
    end
end

function FR:StartKioskSweep(opts)
    opts = opts or {}
    if opts.silent and IsGuildBrowserShowing() then
        return
    end
    local st = self.kioskSweepState
    if st and st.active then
        if st.isSilent and not opts.silent then
            -- Promote silent background sweep to visible mode and report live progress
            st.isSilent = false
            local phaseStr = (st.phase == "guild_finder")
                and string.format("Phase 1/2 (Guild Finder Pass %d/%d: %s)", st.passIndex or 0, st.totalPasses or 9, st.currentPassLabel or "")
                or string.format("Phase 2/2 (Probing Guild IDs %d/%d)", st.probeIndex or 0, st.totalProbes or 0)
            self.PrintChat(string.format("Tamriel Kiosk Sweep is actively running: %s — %s kiosks harvested so far (%s/291 week total). Click again to stop.",
                 self.ColorText(phaseStr, "FFD700"),
                self.ColorText(tostring(st.kiosksHarvested or 0), "00FFCC"),
                self.ColorText(tostring(self:GetCurrentWeekKioskCount()), "59E08A")))
            self:UpdateSweepButtonUI()
            return
        end
        if not opts.silent then
            self:StopKioskSweep("stopped by user")
        end
        return
    end

    -- Scan our own guilds' kiosks (pure local read, zero network packets) and sync AwesomeGuildStore if present.
    -- Note: Do NOT call PollActiveGuildBids() here so we never send burst C++ packets alongside GuildFinderRequestSearch!
    self:ScanOwnedKiosks()
    self:SyncFromAwesomeGuildStore()

    local passes = BuildGuildFinderPasses()
    self.kioskSweepState = {
        active = true,
        isSilent = opts.silent == true,
        forceProbe = opts.forceProbe == true or opts.probeOnly == true,
        phase = opts.probeOnly and "guild_probe" or "guild_finder",
        passes = passes,
        passIndex = 0,
        totalPasses = #passes,
        currentPassLabel = "",
        probeIndex = 0,
        totalProbes = 0,
        networkProbesSent = 0,
        currentGuildId = nil,
        currentGuildName = "",
        kiosksHarvested = 0,
        newOrUpdated = 0,
        startedAt = GetTimeStamp(),
        lastCompletedTs = (st and st.lastCompletedTs) or 0,
        lastHarvested = (st and st.lastHarvested) or 0,
        lastNewOrUpdated = (st and st.lastNewOrUpdated) or 0,
        seenGuildIds = {},
        probeQueue = {},
        pendingGuildId = nil,
        probeRetries = 0,
    }

    if not opts.silent then
        self.PrintChat(string.format("Initiating Polite Tamriel Kiosk Sweep (%d Guild Finder passes @ 3.2s pacing + capped 18-guild probe batch)... Watch live progress on the Fissal Relay HUD!",
            #passes))
    end

    self:UpdateSweepButtonUI()

    if opts.probeOnly then
        self:StartKnownGuildProbe()
    else
        self:ExecuteNextGuildFinderPass()
    end
end

--[[ =========================================================================
     AWESOMEGUILDSTORE & INITIALIZATION HOOKS
========================================================================= ]]--

function FR:SyncFromAwesomeGuildStore()
    if not self.savedVars then return 0 end
    if not self.savedVars.knownTradingGuilds then
        self.savedVars.knownTradingGuilds = {}
    end

    local added = 0
    -- Seed from embedded 636 PC-NA trading guilds
    for gId, gName in pairs(self.SEED_TRADING_GUILDS) do
        if not self.savedVars.knownTradingGuilds[gId] then
            self.savedVars.knownTradingGuilds[gId] = gName
            added = added + 1
        end
    end

    -- Also harvest any guildId -> guildName mappings from AwesomeGuildStore if loaded
    if AwesomeGuildStore and AwesomeGuildStore.internal and AwesomeGuildStore.internal.guildIdMapping then
        local mapping = AwesomeGuildStore.internal.guildIdMapping
        if type(mapping.idToName) == "table" then
            for gId, gName in pairs(mapping.idToName) do
                local numId = tonumber(gId)
                if numId and numId > 0 and type(gName) == "string" and gName ~= "" and not self.savedVars.knownTradingGuilds[numId] then
                    self.savedVars.knownTradingGuilds[numId] = CleanEsoString(gName)
                    added = added + 1
                end
            end
        end
    end

    return added
end

function FR:InitializeKioskReconEngine()
    if not self.savedVars then return end
    if not self.savedVars.knownTradingGuilds then
        self.savedVars.knownTradingGuilds = {}
    end

    -- 1. Seed known trading guilds & purge any non-directory banker NPCs from savedVars.kiosks
    self:SyncFromAwesomeGuildStore()

    if self.savedVars.kiosks then
        local toNormalize = {}
        for key, obs in pairs(self.savedVars.kiosks) do
            table.insert(toNormalize, { key = key, obs = obs })
        end
        for _, item in ipairs(toNormalize) do
            local k = item.key
            local obs = item.obs
            local resolved = self:ResolveCanonicalKiosk(obs.trader or k, obs.city, obs.zone)
            if not resolved or not resolved.matchedDirectory or self:IsBlacklistedTrader(resolved.trader) then
                -- Purge non-kiosk banker NPCs (e.g., Slera Cinna, Kargiz)
                self.savedVars.kiosks[k] = nil
            elseif obs.guildName == "Vacant" and (obs.source == "Guild Finder Sweep" or obs.source == "Guild ID Probe") then
                -- Repair guild 536021 ("Available Trader Kiosk") previously misflagged as Vacant during sweep
                obs.guildId = (tonumber(obs.guildId) and tonumber(obs.guildId) > 0) and tonumber(obs.guildId) or 536021
                obs.guildName = self:NormalizeKioskGuildName(obs.guildName, obs.guildId, obs.source)
            elseif k ~= resolved.trader or obs.zone == "Owned Guild Stall" or obs.city == "Guild Ledger" or obs.city ~= resolved.city then
                -- Upgrade "<Trader> in <City>" or outdated city/spelling to canonical TraderKiosks entry
                self.savedVars.kiosks[k] = nil
                local existing = self.savedVars.kiosks[resolved.trader]
                if not existing or (tonumber(obs.timestamp) or 0) >= (tonumber(existing.timestamp) or 0) then
                    self.savedVars.kiosks[resolved.trader] = {
                        trader = resolved.trader,
                        guildId = tonumber(obs.guildId) or 0,
                        guildName = self:NormalizeKioskGuildName(obs.guildName, obs.guildId, obs.source),
                        zone = resolved.zone,
                        city = resolved.city,
                        x = obs.x or "0.0000",
                        y = obs.y or "0.0000",
                        timestamp = tonumber(obs.timestamp) or GetTimeStamp(),
                        observedBy = obs.observedBy or GetDisplayName(),
                        source = obs.source or "Owned Ledger",
                    }
                end
            end
        end
    end

    -- 2. Register Guild Finder & Guild Info Request Events
    if EVENT_GUILD_FINDER_SEARCH_COMPLETE then
        EVENT_MANAGER:RegisterForEvent(self.name .. "_GFSearch", EVENT_GUILD_FINDER_SEARCH_COMPLETE, function(eventCode, searchId)
            self:OnGuildFinderSearchComplete(eventCode, searchId)
        end)
    end
    if EVENT_GUILD_INFO_REQUEST_COMPLETE then
        EVENT_MANAGER:RegisterForEvent(self.name .. "_GFInfo", EVENT_GUILD_INFO_REQUEST_COMPLETE, function(eventCode, guildId)
            self:OnGuildInfoRequestComplete(eventCode, guildId)
        end)
    end

    -- 3. Register Passive Reticle & Chatter / Bidding Window Events
    if EVENT_RETICLE_TARGET_CHANGED then
        EVENT_MANAGER:RegisterForEvent(self.name .. "_ReticleKiosk", EVENT_RETICLE_TARGET_CHANGED, function()
            self:InspectUnitKiosk("reticleover", "Reticle Recon", false)
        end)
    end
    if EVENT_GUILD_NAME_AVAILABLE then
        EVENT_MANAGER:RegisterForEvent(self.name .. "_GuildNameAvail", EVENT_GUILD_NAME_AVAILABLE, function()
            self:InspectUnitKiosk("reticleover", "Reticle Recon", false)
            self:InspectUnitKiosk("interact", "Interact Recon", false)
        end)
    end
    if EVENT_GUILD_ID_CHANGED then
        EVENT_MANAGER:RegisterForEvent(self.name .. "_GuildIdChanged", EVENT_GUILD_ID_CHANGED, function()
            self:InspectUnitKiosk("reticleover", "Reticle Recon", false)
        end)
    end
    if EVENT_CHATTER_BEGIN then
        EVENT_MANAGER:RegisterForEvent(self.name .. "_ChatterKiosk", EVENT_CHATTER_BEGIN, function()
            self:InspectUnitKiosk("interact", "Chatter Recon", false)
        end)
    end
    if EVENT_GUILD_KIOSK_CONSIDER_BID_START then
        EVENT_MANAGER:RegisterForEvent(self.name .. "_ConsiderBid", EVENT_GUILD_KIOSK_CONSIDER_BID_START, function()
            self:InspectUnitKiosk("interact", "Bid Window Recon", false)
        end)
    end
    if EVENT_GUILD_KIOSK_CONSIDER_PURCHASE_START then
        EVENT_MANAGER:RegisterForEvent(self.name .. "_ConsiderPurchase", EVENT_GUILD_KIOSK_CONSIDER_PURCHASE_START, function()
            self:InspectUnitKiosk("interact", "Hire Window Recon", false)
        end)
    end

    -- 4. Register Active Bids Response Event
    if EVENT_GUILD_KIOSK_ACTIVE_BIDS_RESPONSE then
        EVENT_MANAGER:RegisterForEvent(self.name .. "_ActiveBids", EVENT_GUILD_KIOSK_ACTIVE_BIDS_RESPONSE, function(eventCode, guildId, result)
            self:OnActiveBidsResponse(guildId, result)
        end)
    end

    -- 5. Auto-trigger silent Guild Finder Sweep after Tuesday Reset if current week coverage is low
    zo_callLater(function()
        self:PollActiveGuildBids()
        local weekCount = self:GetCurrentWeekKioskCount()
        if weekCount < 25 and (not self.savedVars.settings or self.savedVars.settings.autoSweepKiosksOnReset ~= false) then
            self:StartKioskSweep({ silent = true })
        end
    end, 22000)
end
