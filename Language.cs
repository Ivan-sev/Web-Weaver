using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Windows.Shapes;
using System.Xml.Linq;
using WebWeaver.Services;

namespace WebWeaver
{
    public static class Language
    {
        // Кэш свойств Lang: имя -> PropertyInfo (используют ReadLangFile и Get)
        private static readonly Dictionary<string, PropertyInfo> LangProps =
            typeof(Lang)
                .GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(p => p.CanWrite)
                .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, object?> LangDefaults =
            LangProps.ToDictionary(p => p.Key, p => p.Value.GetValue(null));

        private static Dictionary<string, string> LangDict = new Dictionary<string, string>();

        public static void LoadLangList()
        {
            if (Directory.Exists(SettingsManager.PathLang))
            {
                string[] files = Directory.GetFiles(SettingsManager.PathLang);

                foreach (string file in files)
                {
                    if (!file.Contains(".lng")) { continue; }

                    string firstLine = File.ReadLines(file).FirstOrDefault()??"";
                    int version = int.Parse(Assembly.GetExecutingAssembly().GetName().Version.ToString().Replace(".", "").Trim('0'));
                    bool old = false;
                    if (firstLine.Contains("#version:"))
                    {
                        old = int.Parse(CutPieceText(firstLine, "#version:").Replace(".", "").Trim('0')) == version ? false : true;
                    }

                    if (firstLine.Contains("#language:"))
                    {
                        var Dict = CutPieceText(firstLine, "#language:").Replace(" ", "").Split(',');

                        foreach (var item in Dict)
                        {
                            LangDict.Add((old ? "!" : "") + item, file);
                        }
                    }
                }
            }
        }

        private static string CutPieceText(string FullText, string Tag)
        {
            return FullText.Substring(FullText.IndexOf(Tag) + Tag.Length, FullText.IndexOf("#", FullText.IndexOf(Tag) + Tag.Length) > 0 ? FullText.IndexOf("#", FullText.IndexOf(Tag) + Tag.Length) - (FullText.IndexOf(Tag) + Tag.Length) : FullText.Length - (FullText.IndexOf(Tag) + Tag.Length)).Trim(' ');
        }

        public static string FormattedDataLang(string Lang)
        {
            var Lang0 = CountryConverter.ConvertCountry(Lang, OutputFormat.Alpha3) ?? "";
            var Lang1 = CountryConverter.ConvertCountry(Lang, OutputFormat.LanguageEnglish) ?? "";
            var Lang2 = CountryConverter.ConvertCountry(Lang, OutputFormat.LanguageNative) ?? "";

            string result = string.Empty;
            List<string> list = new List<string>(LangDict.Keys);
            list.Sort();

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].ToLower() == Lang0.ToLower() ||
                    list[i].ToLower() == Lang1.ToLower() ||
                    list[i].ToLower() == Lang2.ToLower()) 
                { result += "*"; }

                result += list[i];

                if (i < list.Count - 1) result += ",";
            }

            return result;
        }

        /// <summary>
        /// Читать файл локализации (например, rus.txt), кроме первой строки,
        /// и записать все значения в свойства Lang.
        /// Формат: Ключ = "значение",   (кавычки и запятая в конце необязательны)
        /// Массивы: Ключ = "элемент 1", "элемент 2",
        /// </summary>
        public static bool ReadLangFile(string filePath)
        {
            if (!File.Exists(filePath))
                return false;

            foreach (var kv in LangDefaults)
                LangProps[kv.Key].SetValue(null, kv.Value);

            foreach (string rawLine in File.ReadLines(filePath, Encoding.UTF8).Skip(1)) // кроме первой строки
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = line.Substring(0, eq).Trim();
                string valuePart = line.Substring(eq + 1).Trim().TrimEnd(',').Trim();

                if (!LangProps.TryGetValue(key, out PropertyInfo? prop))
                    continue; // в Lang нет такого свойства — пропускаем

                // все фрагменты в кавычках: "а", "б" -> [а, б]
                var parts = Regex.Matches(valuePart, "\"([^\"]*)\"")
                                 .Select(m => m.Groups[1].Value)
                                 .ToList();

                if (prop.PropertyType == typeof(string[]))
                {
                    prop.SetValue(null, parts.Count > 0
                        ? parts.ToArray()
                        : valuePart.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray());
                }
                else if (prop.PropertyType == typeof(string))
                {
                    prop.SetValue(null, parts.Count > 0 ? parts[0] : valuePart);
                }
            }

            return true;
        }

        /// <summary>
        /// Получить перевод по тексту (имя свойства Lang)
        /// </summary>
        public static string Get(string Text)
        {
            if (string.IsNullOrWhiteSpace(Text) || !LangProps.TryGetValue(Text.Trim(), out PropertyInfo? prop))
                return string.Empty;

            return prop.GetValue(null) switch
            {
                string s => s,
                string[] arr => string.Join("|", arr),
                _ => string.Empty
            };
        }

        /// <summary>
        /// Установить текущия язык интерфейса
        /// </summary>
        /// <param name="Lang"></param>
        public static bool Set(string Lang)
        {
            if (LangDict.Count > 0)
            {
                string PathLangFile = "";

                if (LangDict.ContainsKey(Lang)) 
                { PathLangFile = LangDict[Lang]; }
                else if (LangDict.ContainsKey(CountryConverter.ConvertCountry(Lang, OutputFormat.Alpha2) ?? ""))
                { PathLangFile = LangDict[CountryConverter.ConvertCountry(Lang, OutputFormat.Alpha2)]; }
                else if (LangDict.ContainsKey(CountryConverter.ConvertCountry(Lang, OutputFormat.Alpha3) ?? ""))
                { PathLangFile = LangDict[CountryConverter.ConvertCountry(Lang, OutputFormat.Alpha3)]; }
                else if (LangDict.ContainsKey(CountryConverter.ConvertCountry(Lang, OutputFormat.EnglishFullName) ?? ""))
                { PathLangFile = LangDict[CountryConverter.ConvertCountry(Lang, OutputFormat.EnglishFullName)]; }
                else if (LangDict.ContainsKey(CountryConverter.ConvertCountry(Lang, OutputFormat.EnglishName) ?? ""))
                { PathLangFile = LangDict[CountryConverter.ConvertCountry(Lang, OutputFormat.EnglishName)]; }
                else if (LangDict.ContainsKey(CountryConverter.ConvertCountry(Lang, OutputFormat.LanguageEnglish) ?? ""))
                { PathLangFile = LangDict[CountryConverter.ConvertCountry(Lang, OutputFormat.LanguageEnglish)]; }
                else if (LangDict.ContainsKey(CountryConverter.ConvertCountry(Lang, OutputFormat.LanguageNative) ?? ""))
                { PathLangFile = LangDict[CountryConverter.ConvertCountry(Lang, OutputFormat.LanguageNative)]; }
                else if (LangDict.ContainsKey(CountryConverter.ConvertCountry(Lang, OutputFormat.NativeFullName) ?? ""))
                { PathLangFile = LangDict[CountryConverter.ConvertCountry(Lang, OutputFormat.NativeFullName)]; }
                else if (LangDict.ContainsKey(CountryConverter.ConvertCountry(Lang, OutputFormat.NativeName) ?? ""))
                { PathLangFile = LangDict[CountryConverter.ConvertCountry(Lang, OutputFormat.NativeName)]; }
                else { return false; }

                return ReadLangFile(PathLangFile);
            }
            else
            {
                return false;
            }
        }
    }

    public static class Lang
    {
        #region Общие параметры панели
        public static string PanelName { get; set; } = "Имя ноды";
        public static string PanelCancel { get; set; } = "Отмена";
        public static string PanelSave { get; set; } = "💾 Сохранить";
        public static string DefaultName { get; set; } = "Новая нода";
        #endregion

        #region Параметры ноды
        public static string NodeNew { get; set; } = "✨ Новая нода";
        public static string NodeEdit { get; set; } = "✏️ Редактировать ноду";
        public static string NodeText { get; set; } = "Текст ноды";
        public static string NodeBackgroundColor { get; set; } = "Цвет фона";
        public static string NodeHeadingColor { get; set; } = "Цвет заголовка";
        public static string NodeTextColor { get; set; } = "Цвет текста";
        public static string NodeFont { get; set; } = "Шрифт";
        public static string NodeFontSize { get; set; } = "Размер шрифта";
        public static string NodeImage { get; set; } = "Картинка (путь к файлу)";
        public static string NodeSelectImage { get; set; } = "Выбрать картинку";
        public static string NodeAllFiles { get; set; } = "Изображения{&}Все файлы";
        #endregion

        #region Блокнот
        public static string NotepadName { get; set; } = "📖 Блокнот";
        public static string NotepadLinkedNodes { get; set; } = "Связанные ноды";
        public static string NotepadTextNode { get; set; } = "Текст ноды";
        public static string NotepadEditNode { get; set; } = "Блокнот";
        public static string NotepadViewNode { get; set; } = "Предпросмотр";
        public static string NotepadEdit { get; set; } = "Изменить";
        public static string NotepadErrorNodeSearch { get; set; } = "Нода не найдена";
        #endregion

        #region ПКМ
        public static string MouseCollapseSelectionNode { get; set; } = "📦 Сжать выделенное в ноду ({&})";
        public static string MouseNewNode { get; set; } = "➕ Создать ноду";
        public static string MouseAppFile { get; set; } = "📂 Добавить ноду из карты (.wwmap)…";
        public static string MouseCollapseMapNode { get; set; } = "🗜 Сжать всю карту в одну ноду";
        #endregion

        #region ПКМ по ноде
        public static string MouseNodeOpenMap { get; set; } = "🗺 Открыть карту ноды";
        public static string MouseNodeSetHubMap { get; set; } = "🌐 Сделать картой-узлом";
        public static string MouseNodeCollapseNode { get; set; } = "📦 Сжать ветку в ноду";
        public static string MouseNodeEdit { get; set; } = "✏️ Редактировать";
        public static string MouseNodeOpenNotepad { get; set; } = "📖 Открыть блокнот";
        public static string MouseNodeDuplicate { get; set; } = "⧉ Дублировать";
        public static string MouseNodeStartConnection { get; set; } = "🔗 Начать соединение";
        public static string MouseNodeRemoveAllLinks { get; set; } = "✂️ Удалить все связи";
        public static string MouseNodeQuicklyChangeColor { get; set; } = "🎨 Быстро изменить цвет заголовка";
        public static string MouseNodeDeleteNode { get; set; } = "🗑 Удалить ноду";
        #endregion

        #region Дерево
        public static string NodeTreeTitle { get; set; } = "🌳 Дерево нод";
        public static string NodeTreeYourLocation { get; set; } = "Ваше местоположение";
        public static string NodeTreeStartingPoint { get; set; } = "Точка старта";
        public static string NodeTreePointUp { get; set; } = "⬆ На уровень выше";
        public static string NodeTreeClue { get; set; } = "Кликните по ноде, чтобы увидеть её ветку";
        #endregion

        #region История
        public static string HistoryTitle { get; set; } = "⏳ История";
        public static string HistoryClue { get; set; } = "История изменений карты";
        #endregion

        #region Очистить все
        public static string ClearAllWarning { get; set; } = "Все ноды и связи будут удалены. Действие нельзя отменить.";
        public static string ClearAllConfirmation { get; set; } = "Вы уверены?";
        #endregion

        #region Поиск
        public static string FindNodeSearchText { get; set; } = "Поиск по тексту";
        public static string FindNodeFoundText { get; set; } = "Текст поиска";
        public static string FindNode { get; set; } = "Найти ноду";
        public static string FindNodeNodeName { get; set; } = "Введите имя ноды:";
        public static string FindNodeFind { get; set; } = "Найти";
        public static string FindNodeNodeNotFound { get; set; } = "Нода «{&}» не найдена.";
        public static string FindNodeLevelPrefix { get; set; } = "lvl:";
        #endregion

        #region Drop
        public static string DropTitle { get; set; } = "Файл карты";
        public static string DropWhat { get; set; } = "что сделать?";
        public static string DropMap { get; set; } = "Открыть как карту";
        public static string DropNode { get; set; } = "Добавить как ноду";
        public static string DropCancel { get; set; } = "Отмена";
        public static string DropNoMap { get; set; } = "не файл карты";
        #endregion

        #region Button
        public static string[] Button1 { get; set; } = { "Файл", "Файлы" };
        public static string[] Button2 { get; set; } = { "Инструменты", "Инструменты" };
        public static string[] Button3 { get; set; } = { "Вид", "Вид" };
        public static string[] Button4 { get; set; } = { "", "" };
        public static string[] Button5 { get; set; } = { "", "" };
        public static string[] Button6 { get; set; } = { "", "" };
        public static string[] Button7 { get; set; } = { "", "" };
        public static string[] Button8 { get; set; } = { "", "" };
        public static string[] Button9 { get; set; } = { "", "" };
        public static string[] BtnNewNode { get; set; } = { "＋ Новая нода", "Создать новую ноду (Insert)" };
        public static string[] AddNodeFromMapFile { get; set; } = { "📂 Новая нода карта", "Добавить ноду из карты (.wwmap)" };
        public static string[] BtnSave { get; set; } = { "💾 Сохранить", "Сохранить карту (Ctrl+S)" };
        public static string[] BtnSaveAs { get; set; } = { "💾 Сохранить как", "Сохранить карту как (Ctrl+Shift+S)" };
        public static string[] BtnOpen { get; set; } = { "📂 Открыть", "Открыть карту (Ctrl+O)" };
        public static string[] ShowNodeTree { get; set; } = { "🌳 Дерево", "Дерево нод: кто к кому принадлежит" };
        public static string[] BtnHistory { get; set; } = { "⏳ История", "История изменений" };
        public static string[] BtnClearAlll { get; set; } = { "🗑 Очистить всё", "Очистить всё (Ctrl+Del)" };
        public static string[] BtnFindNode { get; set; } = { "🔍 Поиск", "Поиск нод в проекте (Ctrl+F)" };
        public static string[] BtnSettings { get; set; } = { "⚙️ Настройки", "Настройки приложения" };
        public static string[] BtnResetView { get; set; } = { "⊡ Сброс вида", "Сбросить масштаб и положение (Ctrl+Home)" };
        public static string[] BtnZoomIn { get; set; } = { "🔍＋", "Приблизить (Ctrl+)" };
        public static string[] BtnZoomOut { get; set; } = { "🔍−", "Отдалить (Ctrl-)" };
        #endregion

        #region Блокнот (дополнение к существующему региону)
        /// <summary>Подсказка по синтаксису ссылок в блокноте</summary>
        public static string NotepadLinkHint { get; set; } = "Ссылка на ноду: [отображаемый текст]:[имя ноды]  •  Интернет: [отображаемый текст]:[https://...]";
        #endregion

        #region Нода (контрол на канве)
        public static string NodeDragTooltip { get; set; } = "Перетащить";
        public static string NodePortConnectRight { get; set; } = "Соединить (правый порт)";
        public static string NodePortConnectLeft { get; set; } = "Соединить (левый порт)";
        public static string NodeResizeTooltip { get; set; } = "Изменить размер";
        #endregion

        #region Палитра цветов
        public static string ColorPickerTitle { get; set; } = "Выбрать цвет";
        public static string ColorPickerHint { get; set; } = "Выбрать цвет или ввести hex код";
        public static string ColorPickerHex { get; set; } = "Hex:";
        public static string ColorPickerOk { get; set; } = "✓ OK";
        public static string ColorPickerInvalidHex { get; set; } = "Пожалуйста, введите корректный hex код (например: #FF00FF)";
        public static string ErrorTitle { get; set; } = "Ошибка";
        #endregion

        #region Панель управления
        public static string ControlPanelTitle { get; set; } = "Панель";
        #endregion

        #region Регион / страна
        public static string RegionInvalidCode { get; set; } = "Неверный код";
        public static string RegionUnknownCountry { get; set; } = "Неизвестная страна ({&})";
        public static string RegionInvalidCountryCode { get; set; } = "Неверный код страны";
        public static string RegionUnknownLanguage { get; set; } = "Неизвестный язык ({&})";
        public static string RegionDetectError { get; set; } = "Ошибка определения языка";
        #endregion

        #region Статус-бар
        public static string StatusReady { get; set; } = "Готово";
        public static string StatusReadyHint { get; set; } = "Готово. ПКМ по карте — создать ноду.";
        public static string StatusSelectionCleared { get; set; } = "Выделение снято";
        public static string StatusRubberCancelled { get; set; } = "Выделение отменено.";
        public static string StatusConnectionCancelled { get; set; } = "Соединение отменено.";
        public static string StatusConnectionPickTarget { get; set; } = "Выбрана нода «{&}». Кликните на порт другой ноды.";
        public static string StatusConnectionSelf { get; set; } = "Нельзя соединить ноду саму с собой.";
        public static string StatusConnectionExists { get; set; } = "Такая связь уже существует.";
        public static string StatusConnectionCreated { get; set; } = "Связь создана: «{&}» → «{*}».";
        public static string StatusLinkRemoved { get; set; } = "Связь удалена (ПКМ по линии).";
        public static string StatusAllLinksRemoved { get; set; } = "Все связи ноды «{&}» удалены.";
        public static string StatusNodeDeleted { get; set; } = "Нода «{&}» удалена.";
        public static string StatusNodeUpdated { get; set; } = "Нода «{&}» обновлена.";
        public static string StatusGoToNode { get; set; } = "Переход к ноде «{&}».";
        public static string StatusNoSelection { get; set; } = "Нет выделенных нод.";
        public static string StatusGroupCount { get; set; } = "Выделено нод: {&}";
        public static string StatusNodesDeleted { get; set; } = "Удалено нод: {&}";
        public static string StatusCopiedNodes { get; set; } = "Скопировано нод: {&} — вставьте Ctrl+V.";
        public static string StatusCutNodes { get; set; } = "Вырезано нод: {&} — вставьте Ctrl+V.";
        public static string StatusClipboardEmpty { get; set; } = "Буфер обмена пуст.";
        public static string StatusPastedNodes { get; set; } = "Вставлено нод: {&}";
        public static string StatusMapCleared { get; set; } = "Карта очищена.";
        public static string StatusInRootMap { get; set; } = "Вы в корневой карте.";
        public static string StatusAlreadyInRootMap { get; set; } = "Вы уже в корневой карте.";
        public static string StatusMapOpened { get; set; } = "Открыта карта: {&}";
        public static string StatusNodeMapOpened { get; set; } = "Открыта карта ноды «{&}». ПКМ по фону — создать ноду внутри.";
        public static string StatusMapEmpty { get; set; } = "Карта пуста — сжимать нечего.";
        public static string StatusSelectNodesFirst { get; set; } = "Сначала выделите ноды: Ctrl+клик или рамка.";
        public static string StatusNoOutgoingLinks { get; set; } = "У ноды нет исходящих связей — сжимать нечего.";
        public static string StatusMapCompressed { get; set; } = "Карта сжата в ноду «{&}» ({*} нод внутри). Двойной клик — открыть.";
        public static string StatusSelectionCompressed { get; set; } = "Создан узел «{&}»: двойной клик открывает вложенную карту.";
        public static string StatusBranchCompressed { get; set; } = "Ветка ({&} нод) сжата в ноду «{*}».";
        public static string StatusSaved { get; set; } = "Сохранено: {&}";
        public static string StatusOpenedFile { get; set; } = "Открыто: {&} ({*} нод)";
        public static string StatusNodeNotFoundById { get; set; } = "Нода {&} не найдена";
        public static string StatusNodeNotOpenedById { get; set; } = "Нода {&} найдена, но открыть её не удалось";
        public static string StatusAutosaved { get; set; } = "Автосохранение выполнено.";
        public static string StatusSettingsSaved { get; set; } = "Настройки сохранены.";
        public static string StatusSettingsCancelled { get; set; } = "Изменения настроек отменены.";
        public static string StatusRestoreFailed { get; set; } = "Не удалось восстановить состояние: {&}";
        public static string StatusUndoNothing { get; set; } = "Отменять нечего.";
        public static string StatusRedoNothing { get; set; } = "Повторять нечего.";
        public static string StatusJumpTo { get; set; } = "Возврат к: {&}";
        public static string StatusPressButton { get; set; } = "Нажата кнопка панели: {&}";
        public static string StatusMapNodeCreated { get; set; } = "Добавлена нода-карта «{&}» ({*} нод внутри).";
        #endregion

        #region История (заголовки операций)
        public static string HistoryInitial { get; set; } = "Начальное состояние";
        public static string HistoryNodeCreated { get; set; } = "Создание ноды";
        public static string HistoryNodeEdited { get; set; } = "Изменение ноды «{&}»";
        public static string HistoryNodeUpdated { get; set; } = "Нода обновлена: «{&}»";
        public static string HistoryNodeDeleted { get; set; } = "Нода удалена: «{&}»";
        public static string HistoryNodeDuplicated { get; set; } = "Дублирование ноды «{&}»";
        public static string HistoryLinkDeleted { get; set; } = "Связь удалена: «{&}» → «{*}»";
        public static string HistoryAllLinksDeleted { get; set; } = "Все связи ноды «{&}» удалены";
        public static string HistoryNodeMoved { get; set; } = "Перемещение ноды";
        public static string HistoryNodesMoved { get; set; } = "Перемещение нод ({&})";
        public static string HistoryNodesDeleted { get; set; } = "Удаление нод ({&})";
        public static string HistoryNodesPasted { get; set; } = "Вставка нод ({&})";
        public static string HistoryGoToNode { get; set; } = "Переход к ноде: «{&}»";
        public static string HistoryConnectionCancelled { get; set; } = "Соединение отменено.";
        public static string HistoryConnectionCreated { get; set; } = "Связь создана: «{&}» → «{*}»";
        public static string HistoryMapCleared { get; set; } = "Карта очищена.";
        public static string HistoryMapCompressed { get; set; } = "Карта сжата в ноду: «{&}»";
        public static string HistorySelectionCompressed { get; set; } = "Сжать выделенное в ноду";
        public static string HistoryBranchCompressed { get; set; } = "Ветка сжата в ноду: «{&}»";
        public static string HistoryMapOpened { get; set; } = "Открыта карта: {&}";
        public static string HistoryUndone { get; set; } = "Отменено: {&}";
        public static string HistoryRedone { get; set; } = "Повторено: {&}";
        public static string HistoryJumpedBack { get; set; } = "Возвращено к: {&}";
        public static string HistoryMapNodeCreated { get; set; } = "Добавлена нода-карта «{&}»";
        #endregion

        #region Карты-узлы / копии
        public static string MapRootTitle { get; set; } = "Корень";
        public static string MapCompressedName { get; set; } = "Сжатая карта";
        public static string MapWrapperName { get; set; } = "Карта ({&})";
        public static string NodeCopySuffix { get; set; } = " (копия)";
        #endregion

        #region Диалоги и файлы
        public static string DialogConfirmation { get; set; } = "Подтверждение";
        public static string ClearAllQuestion { get; set; } = "Очистить всю карту? Несохранённые данные будут потеряны.";
        public static string FileDialogTitleOpen { get; set; } = "Открыть карту";
        public static string FileDialogTitleSave { get; set; } = "Сохранить карту";
        public static string FileDialogTitleAddFromMap { get; set; } = "Добавить ноду из карты";
        public static string FileDialogMapFilter { get; set; } = "Карты узлов (*.wwmap;*.gnmap)|*.wwmap;*.gnmap|Все файлы|*.*";
        public static string FileDialogWwmapFilter { get; set; } = "Карта узлов (*.wwmap)|*.wwmap|Все файлы|*.*";
        public static string ErrorFileNotMap { get; set; } = "Файл пуст или не является картой узлов.";
        public static string ErrorOpenMap { get; set; } = "Не удалось открыть карту:\n{&}";
        public static string SettingsFileTitle { get; set; } = "Файл настроек";
        public static string Error { get; set; } = "Ошибка";
        #endregion

        #region Окна Дерево / История
        public static string NodeTreeYouAreHere { get; set; } = "Вы здесь: {&}";
        public static string NodeTreeRoot { get; set; } = "🏠 Корень";
        public static string NodeTreeGoRoot { get; set; } = "🏠 В корень";
        public static string NodeTreeHintDoubleClick { get; set; } = "Двойной клик по 🗺 — перейти внутрь этой карты.";
        public static string HistoryWindowTitle { get; set; } = "История операций";
        public static string HistoryHintDoubleClick { get; set; } = "Двойной клик — перейти к этому состоянию";
        #endregion

        #region Настройки (панель)
        public static string SettingsTitle { get; set; } = "Настройки";
        public static string SettingsTooltipPending { get; set; } = "Настройки (но ещё не работает)";
        public static string SettingsSectionGeneral { get; set; } = "Общие";
        public static string SettingsLanguage { get; set; } = "Язык";
        public static string SettingsLanguageDesc { get; set; } = "Язык интерфейса";
        public static string SettingsTheme { get; set; } = "Тема";
        public static string SettingsThemeDesc { get; set; } = "Применяется сразу. «Кастомная» правится в Settings.json";
        public static string ThemeDark { get; set; } = "Тёмная";
        public static string ThemeLight { get; set; } = "Светлая";
        public static string ThemeCustom { get; set; } = "Кастомная";
        public static string SettingsSectionAutosave { get; set; } = "Автосохранение";
        public static string SettingsAutosaveEnable { get; set; } = "Включить";
        public static string SettingsAutosaveEnableDesc { get; set; } = "Сохранять карту автоматически";
        public static string SettingsIntervalCaption { get; set; } = "Интервал: {&}";
        public static string SettingsIntervalName { get; set; } = "Интервал, сек";
        public static string SettingsIntervalDesc { get; set; } = "Пауза между автосохранениями";
        public static string SettingsChangesCaption { get; set; } = "После изменений: {&}";
        public static string SettingsChangesName { get; set; } = "После изменений";
        public static string SettingsChangesDesc { get; set; } = "Сохранять после N изменений";
        public static string SettingsSectionNodeColors { get; set; } = "Цвета нод по умолчанию";
        public static string SettingsNodeBg { get; set; } = "Фон (hex)";
        public static string SettingsNodeBgDesc { get; set; } = "Фон новой ноды, формат #RRGGBB";
        public static string SettingsNodeHeader { get; set; } = "Заголовок (hex)";
        public static string SettingsNodeHeaderDesc { get; set; } = "Цвет шапки новой ноды";
        public static string SettingsNodeText { get; set; } = "Текст (hex)";
        public static string SettingsNodeTextDesc { get; set; } = "Цвет текста в ноде";
        public static string SettingsOpenFile { get; set; } = "Открыть Settings.json";
        public static string SettingsOpenFileDesc { get; set; } = "Цвета тем и параметры правятся в файле";
        public static string SettingsDone { get; set; } = "Готово";
        public static string SettingsDoneDesc { get; set; } = "Применить и записать Settings.json";
        public static string SettingsCancelDesc { get; set; } = "Вернуть сохранённое (перечитывает файл)";
        public static string TimeHours { get; set; } = " ч.";
        public static string TimeMinutes { get; set; } = " мин.";
        public static string TimeSeconds { get; set; } = " сек.";
        #endregion

        #region Дополнительные данные
        public static string MessageBoxCapErrLoadSettings { get; set; } = 
            "Неисправный файл сохраненных настроек!";
        public static string MessageBoxDescErrLoadSettings { get; set; } =
            "Привет! 🖐️\nК сожалению, файл с твоими персональными настройками поврежден. Возможно, последние изменения были некорректно сохранены или внутри оказались неверные данные в JSON-формате, поэтому я не могу их прочитать.\nЯ могу создать файл заново и заполнить его стандартными настройками, как при первом запуске. Восстановить настройки по умолчанию?\n";
        #endregion
    }

    #region CountryConverter
    public enum OutputFormat
    {
        Alpha2,
        Alpha3,
        EnglishName,
        NativeName,
        EnglishFullName,
        NativeFullName,
        LanguageEnglish,
        LanguageNative
    }

    public sealed class CountryData
    {
        /// <summary>ISO 3166-1 alpha-2</summary>
        public string Alpha2 { get; init; } = "";

        /// <summary>ISO 3166-1 alpha-3</summary>
        public string Alpha3 { get; init; } = "";

        /// <summary>Краткое название на английском</summary>
        public string EnglishName { get; init; } = "";

        /// <summary>Краткое название на родном языке</summary>
        public string NativeName { get; init; } = "";

        /// <summary>Полное официальное название на английском (если есть)</summary>
        public string? EnglishFullName { get; init; }

        /// <summary>Полное официальное название на родном языке (если есть)</summary>
        public string? NativeFullName { get; init; }

        /// <summary>Основной язык — по-английски</summary>
        public string LanguageEnglish { get; init; } = "";

        /// <summary>Основной язык — на родном языке</summary>
        public string LanguageNative { get; init; } = "";

        /// <summary>Дополнительные ключи поиска</summary>
        public List<string> SearchKeys { get; init; } = new();
    }

    public static class CountryConverter
    {
        private static readonly List<CountryData> Countries;
        private static readonly Dictionary<string, CountryData> SearchIndex;

        static CountryConverter()
        {
            Countries = new List<CountryData>
        {
            new()
            {
                Alpha2 = "cn", Alpha3 = "chn",
                EnglishName = "China", NativeName = "中国",
                EnglishFullName = "People's Republic of China", NativeFullName = "中华人民共和国",
                LanguageEnglish = "Chinese", LanguageNative = "中文",
                SearchKeys = new() { "китай", "кітай", "китайский", "китайська" }
            },
            new()
            {
                Alpha2 = "in", Alpha3 = "ind",
                EnglishName = "India", NativeName = "भारत",
                EnglishFullName = "Republic of India", NativeFullName = "भारत गणराज्य",
                LanguageEnglish = "Hindi", LanguageNative = "हिन्दी",
                SearchKeys = new() { "индия", "індыя", "індія", "хинди" }
            },
            new()
            {
                Alpha2 = "us", Alpha3 = "usa",
                EnglishName = "United States", NativeName = "United States",
                EnglishFullName = "United States of America", NativeFullName = "United States of America",
                LanguageEnglish = "English", LanguageNative = "English",
                SearchKeys = new() { "сша", "соединенные штаты", "зша", "сполучені штати", "америка" }
            },
            new()
            {
                Alpha2 = "br", Alpha3 = "bra",
                EnglishName = "Brazil", NativeName = "Brasil",
                EnglishFullName = "Federative Republic of Brazil", NativeFullName = "República Federativa do Brasil",
                LanguageEnglish = "Portuguese", LanguageNative = "Português",
                SearchKeys = new() { "бразилия", "бразілія", "бразилія", "португальский" }
            },
            new()
            {
                Alpha2 = "ru", Alpha3 = "rus",
                EnglishName = "Russia", NativeName = "Россия",
                EnglishFullName = "Russian Federation", NativeFullName = "Российская Федерация",
                LanguageEnglish = "Russian", LanguageNative = "Русский",
                SearchKeys = new() { "рф", "расія", "росія", "расійская федэрацыя", "російська федерація", "русский" }
            },
            new()
            {
                Alpha2 = "jp", Alpha3 = "jpn",
                EnglishName = "Japan", NativeName = "日本",
                EnglishFullName = "Japan", NativeFullName = "日本国",
                LanguageEnglish = "Japanese", LanguageNative = "日本語",
                SearchKeys = new() { "япония", "японія", "nihon", "nippon" }
            },
            new()
            {
                Alpha2 = "id", Alpha3 = "idn",
                EnglishName = "Indonesia", NativeName = "Indonesia",
                EnglishFullName = "Republic of Indonesia", NativeFullName = "Republik Indonesia",
                LanguageEnglish = "Indonesian", LanguageNative = "Bahasa Indonesia",
                SearchKeys = new() { "индонезия", "індонезія", "інданезія" }
            },
            new()
            {
                Alpha2 = "ng", Alpha3 = "nga",
                EnglishName = "Nigeria", NativeName = "Nigeria",
                EnglishFullName = "Federal Republic of Nigeria", NativeFullName = "Federal Republic of Nigeria",
                LanguageEnglish = "English", LanguageNative = "English",
                SearchKeys = new() { "нигерия", "нігерыя", "нігерія" }
            },
            new()
            {
                Alpha2 = "mx", Alpha3 = "mex",
                EnglishName = "Mexico", NativeName = "México",
                EnglishFullName = "United Mexican States", NativeFullName = "Estados Unidos Mexicanos",
                LanguageEnglish = "Spanish", LanguageNative = "Español",
                SearchKeys = new() { "мексика", "мексіка" }
            },
            new()
            {
                Alpha2 = "de", Alpha3 = "deu",
                EnglishName = "Germany", NativeName = "Deutschland",
                EnglishFullName = "Federal Republic of Germany", NativeFullName = "Bundesrepublik Deutschland",
                LanguageEnglish = "German", LanguageNative = "Deutsch",
                SearchKeys = new() { "германия", "германія", "нямеччына", "німеччина", "немецкий" }
            },
            new()
            {
                Alpha2 = "vn", Alpha3 = "vnm",
                EnglishName = "Vietnam", NativeName = "Việt Nam",
                EnglishFullName = "Socialist Republic of Vietnam", NativeFullName = "Cộng hòa Xã hội chủ nghĩa Việt Nam",
                LanguageEnglish = "Vietnamese", LanguageNative = "Tiếng Việt",
                SearchKeys = new() { "вьетнам", "в'етнам", "в'єтнам" }
            },
            new()
            {
                Alpha2 = "ph", Alpha3 = "phl",
                EnglishName = "Philippines", NativeName = "Pilipinas",
                EnglishFullName = "Republic of the Philippines", NativeFullName = "Republika ng Pilipinas",
                LanguageEnglish = "Filipino", LanguageNative = "Filipino",
                SearchKeys = new() { "филиппины", "філіпіны", "філіппіни", "tagalog" }
            },
            new()
            {
                Alpha2 = "gb", Alpha3 = "gbr",
                EnglishName = "United Kingdom", NativeName = "United Kingdom",
                EnglishFullName = "United Kingdom of Great Britain and Northern Ireland",
                NativeFullName = "United Kingdom of Great Britain and Northern Ireland",
                LanguageEnglish = "English", LanguageNative = "English",
                SearchKeys = new() { "uk", "великобритания", "вялікабрытанія", "велика британія", "англия", "англія", "great britain" }
            },
            new()
            {
                Alpha2 = "ir", Alpha3 = "irn",
                EnglishName = "Iran", NativeName = "ایران",
                EnglishFullName = "Islamic Republic of Iran", NativeFullName = "جمهوری اسلامی ایران",
                LanguageEnglish = "Persian", LanguageNative = "فارسی",
                SearchKeys = new() { "иран", "іран", "farsi" }
            },
            new()
            {
                Alpha2 = "tr", Alpha3 = "tur",
                EnglishName = "Turkey", NativeName = "Türkiye",
                EnglishFullName = "Republic of Türkiye", NativeFullName = "Türkiye Cumhuriyeti",
                LanguageEnglish = "Turkish", LanguageNative = "Türkçe",
                SearchKeys = new() { "турция", "турцыя", "туреччина" }
            },
            new()
            {
                Alpha2 = "fr", Alpha3 = "fra",
                EnglishName = "France", NativeName = "France",
                EnglishFullName = "French Republic", NativeFullName = "République française",
                LanguageEnglish = "French", LanguageNative = "Français",
                SearchKeys = new() { "франция", "францыя", "франція" }
            },
            new()
            {
                Alpha2 = "kr", Alpha3 = "kor",
                EnglishName = "South Korea", NativeName = "한국",
                EnglishFullName = "Republic of Korea", NativeFullName = "대한민국",
                LanguageEnglish = "Korean", LanguageNative = "한국어",
                SearchKeys = new() { "южная корея", "корея", "паўднёвая карэя", "південна корея", "карэя" }
            },
            new()
            {
                Alpha2 = "eg", Alpha3 = "egy",
                EnglishName = "Egypt", NativeName = "مصر",
                EnglishFullName = "Arab Republic of Egypt", NativeFullName = "جمهورية مصر العربية",
                LanguageEnglish = "Arabic", LanguageNative = "العربية",
                SearchKeys = new() { "египет", "егіпет", "єгипет" }
            },
            new()
            {
                Alpha2 = "it", Alpha3 = "ita",
                EnglishName = "Italy", NativeName = "Italia",
                EnglishFullName = "Italian Republic", NativeFullName = "Repubblica Italiana",
                LanguageEnglish = "Italian", LanguageNative = "Italiano",
                SearchKeys = new() { "италия", "італія" }
            },
            new()
            {
                Alpha2 = "es", Alpha3 = "esp",
                EnglishName = "Spain", NativeName = "España",
                EnglishFullName = "Kingdom of Spain", NativeFullName = "Reino de España",
                LanguageEnglish = "Spanish", LanguageNative = "Español",
                SearchKeys = new() { "испания", "іспанія", "castellano" }
            },
            new()
            {
                Alpha2 = "th", Alpha3 = "tha",
                EnglishName = "Thailand", NativeName = "ประเทศไทย",
                EnglishFullName = "Kingdom of Thailand", NativeFullName = "ราชอาณาจักรไทย",
                LanguageEnglish = "Thai", LanguageNative = "ภาษาไทย",
                SearchKeys = new() { "таиланд", "тайланд", "таїланд" }
            },
            new()
            {
                Alpha2 = "pk", Alpha3 = "pak",
                EnglishName = "Pakistan", NativeName = "پاکستان",
                EnglishFullName = "Islamic Republic of Pakistan", NativeFullName = "اسلامی جمہوریہ پاکستان",
                LanguageEnglish = "Urdu", LanguageNative = "اردو",
                SearchKeys = new() { "пакистан", "пакістан" }
            },
            new()
            {
                Alpha2 = "ca", Alpha3 = "can",
                EnglishName = "Canada", NativeName = "Canada",
                EnglishFullName = "Canada", NativeFullName = "Canada",
                LanguageEnglish = "English", LanguageNative = "English",
                SearchKeys = new() { "канада", "français", "canadian" }
            },
            new()
            {
                Alpha2 = "ar", Alpha3 = "arg",
                EnglishName = "Argentina", NativeName = "Argentina",
                EnglishFullName = "Argentine Republic", NativeFullName = "República Argentina",
                LanguageEnglish = "Spanish", LanguageNative = "Español",
                SearchKeys = new() { "аргентина", "аргенціна" }
            },
            new()
            {
                Alpha2 = "uz", Alpha3 = "uzb",
                EnglishName = "Uzbekistan", NativeName = "Oʻzbekiston",
                EnglishFullName = "Republic of Uzbekistan", NativeFullName = "Oʻzbekiston Respublikasi",
                LanguageEnglish = "Uzbek", LanguageNative = "Oʻzbekcha",
                SearchKeys = new() { "узбекистан", "узбекістан", "ўзбекистон", "узбекский", "ўзбекча" }
            },
            new()
            {
                Alpha2 = "za", Alpha3 = "zaf",
                EnglishName = "South Africa", NativeName = "South Africa",
                EnglishFullName = "Republic of South Africa", NativeFullName = "Republic of South Africa",
                LanguageEnglish = "English", LanguageNative = "English",
                SearchKeys = new() { "юар", "южная африка", "пар", "паўднёвая афрыка", "південна африка", "afrikaans", "zulu" }
            },
            new()
            {
                Alpha2 = "ua", Alpha3 = "ukr",
                EnglishName = "Ukraine", NativeName = "Україна",
                EnglishFullName = "Ukraine", NativeFullName = "Україна",
                LanguageEnglish = "Ukrainian", LanguageNative = "Українська",
                SearchKeys = new() { "украина", "украіна", "український", "украинский" }
            },
            new()
            {
                Alpha2 = "sa", Alpha3 = "sau",
                EnglishName = "Saudi Arabia", NativeName = "السعودية",
                EnglishFullName = "Kingdom of Saudi Arabia", NativeFullName = "المملكة العربية السعودية",
                LanguageEnglish = "Arabic", LanguageNative = "العربية",
                SearchKeys = new() { "саудовская аравия", "саудаўская аравія", "саудівська аравія" }
            },
            new()
            {
                Alpha2 = "co", Alpha3 = "col",
                EnglishName = "Colombia", NativeName = "Colombia",
                EnglishFullName = "Republic of Colombia", NativeFullName = "República de Colombia",
                LanguageEnglish = "Spanish", LanguageNative = "Español",
                SearchKeys = new() { "колумбия", "калумбія", "колумбія" }
            },
            new()
            {
                Alpha2 = "pl", Alpha3 = "pol",
                EnglishName = "Poland", NativeName = "Polska",
                EnglishFullName = "Republic of Poland", NativeFullName = "Rzeczpospolita Polska",
                LanguageEnglish = "Polish", LanguageNative = "Polski",
                SearchKeys = new() { "польша", "польшча", "польща", "польский" }
            },
            new()
            {
                Alpha2 = "my", Alpha3 = "mys",
                EnglishName = "Malaysia", NativeName = "Malaysia",
                EnglishFullName = "Malaysia", NativeFullName = "Malaysia",
                LanguageEnglish = "Malay", LanguageNative = "Bahasa Melayu",
                SearchKeys = new() { "малайзия", "малайзія" }
            },
            new()
            {
                Alpha2 = "dz", Alpha3 = "dza",
                EnglishName = "Algeria", NativeName = "الجزائر",
                EnglishFullName = "People's Democratic Republic of Algeria", NativeFullName = "الجمهورية الجزائرية الديمقراطية الشعبية",
                LanguageEnglish = "Arabic", LanguageNative = "العربية",
                SearchKeys = new() { "алжир", "алжыр" }
            },
            new()
            {
                Alpha2 = "bd", Alpha3 = "bgd",
                EnglishName = "Bangladesh", NativeName = "বাংলাদেশ",
                EnglishFullName = "People's Republic of Bangladesh", NativeFullName = "গণপ্রজাতন্ত্রী বাংলাদেশ",
                LanguageEnglish = "Bengali", LanguageNative = "বাংলা",
                SearchKeys = new() { "бангладеш", "бангладэш", "bangla" }
            },
            new()
            {
                Alpha2 = "ma", Alpha3 = "mar",
                EnglishName = "Morocco", NativeName = "المغرب",
                EnglishFullName = "Kingdom of Morocco", NativeFullName = "المملكة المغربية",
                LanguageEnglish = "Arabic", LanguageNative = "العربية",
                SearchKeys = new() { "марокко", "марока" }
            },
            new()
            {
                Alpha2 = "tw", Alpha3 = "twn",
                EnglishName = "Taiwan", NativeName = "臺灣",
                EnglishFullName = "Republic of China", NativeFullName = "中華民國",
                LanguageEnglish = "Chinese", LanguageNative = "中文",
                SearchKeys = new() { "тайвань", "taiwan" }
            },
            new()
            {
                Alpha2 = "au", Alpha3 = "aus",
                EnglishName = "Australia", NativeName = "Australia",
                EnglishFullName = "Commonwealth of Australia", NativeFullName = "Commonwealth of Australia",
                LanguageEnglish = "English", LanguageNative = "English",
                SearchKeys = new() { "австралия", "аўстралія", "австралія" }
            },
            new()
            {
                Alpha2 = "ve", Alpha3 = "ven",
                EnglishName = "Venezuela", NativeName = "Venezuela",
                EnglishFullName = "Bolivarian Republic of Venezuela", NativeFullName = "República Bolivariana de Venezuela",
                LanguageEnglish = "Spanish", LanguageNative = "Español",
                SearchKeys = new() { "венесуэла", "венесуела" }
            },
            new()
            {
                Alpha2 = "et", Alpha3 = "eth",
                EnglishName = "Ethiopia", NativeName = "ኢትዮጵያ",
                EnglishFullName = "Federal Democratic Republic of Ethiopia", NativeFullName = "የኢትዮጵያ ፌዴራላዊ ዴሞክራሲያዊ ሪፐብሊክ",
                LanguageEnglish = "Amharic", LanguageNative = "አማርኛ",
                SearchKeys = new() { "эфиопия", "эфіопія", "ефіопія" }
            },
            new()
            {
                Alpha2 = "iq", Alpha3 = "irq",
                EnglishName = "Iraq", NativeName = "العراق",
                EnglishFullName = "Republic of Iraq", NativeFullName = "جمهورية العراق",
                LanguageEnglish = "Arabic", LanguageNative = "العربية",
                SearchKeys = new() { "ирак", "ірак", "kurdish", "کوردی" }
            },
            new()
            {
                Alpha2 = "mm", Alpha3 = "mmr",
                EnglishName = "Myanmar", NativeName = "မြန်မာ",
                EnglishFullName = "Republic of the Union of Myanmar", NativeFullName = "ပြည်ထောင်စု သမ္မတ မြန်မာနိုင်ငံတော်",
                LanguageEnglish = "Burmese", LanguageNative = "မြန်မာဘာသာ",
                SearchKeys = new() { "мьянма", "м'янма", "бирма", "burma" }
            },
            new()
            {
                Alpha2 = "pe", Alpha3 = "per",
                EnglishName = "Peru", NativeName = "Perú",
                EnglishFullName = "Republic of Peru", NativeFullName = "República del Perú",
                LanguageEnglish = "Spanish", LanguageNative = "Español",
                SearchKeys = new() { "перу", "quechua", "aymara" }
            },
            new()
            {
                Alpha2 = "nl", Alpha3 = "nld",
                EnglishName = "Netherlands", NativeName = "Nederland",
                EnglishFullName = "Kingdom of the Netherlands", NativeFullName = "Koninkrijk der Nederlanden",
                LanguageEnglish = "Dutch", LanguageNative = "Nederlands",
                SearchKeys = new() { "нидерланды", "голландия", "нідэрланды", "нідерланди", "голландія", "галандыя" }
            },
            new()
            {
                Alpha2 = "ro", Alpha3 = "rou",
                EnglishName = "Romania", NativeName = "România",
                EnglishFullName = "Romania", NativeFullName = "România",
                LanguageEnglish = "Romanian", LanguageNative = "Română",
                SearchKeys = new() { "румыния", "румынія", "румунія", "румынский" }
            },
            new()
            {
                Alpha2 = "kz", Alpha3 = "kaz",
                EnglishName = "Kazakhstan", NativeName = "Қазақстан",
                EnglishFullName = "Republic of Kazakhstan", NativeFullName = "Қазақстан Республикасы",
                LanguageEnglish = "Kazakh", LanguageNative = "Қазақша",
                SearchKeys = new() { "казахстан", "қазақстан", "казахский", "qazaqsha" }
            },
            new()
            {
                Alpha2 = "cl", Alpha3 = "chl",
                EnglishName = "Chile", NativeName = "Chile",
                EnglishFullName = "Republic of Chile", NativeFullName = "República de Chile",
                LanguageEnglish = "Spanish", LanguageNative = "Español",
                SearchKeys = new() { "чили", "чылі", "чилі" }
            },
            new()
            {
                Alpha2 = "tz", Alpha3 = "tza",
                EnglishName = "Tanzania", NativeName = "Tanzania",
                EnglishFullName = "United Republic of Tanzania", NativeFullName = "Jamhuri ya Muungano wa Tanzania",
                LanguageEnglish = "Swahili", LanguageNative = "Kiswahili",
                SearchKeys = new() { "танзания", "танзанія" }
            },
            new()
            {
                Alpha2 = "sd", Alpha3 = "sdn",
                EnglishName = "Sudan", NativeName = "السودان",
                EnglishFullName = "Republic of the Sudan", NativeFullName = "جمهورية السودان",
                LanguageEnglish = "Arabic", LanguageNative = "العربية",
                SearchKeys = new() { "судан" }
            },
            new()
            {
                Alpha2 = "ci", Alpha3 = "civ",
                EnglishName = "Ivory Coast", NativeName = "Côte d'Ivoire",
                EnglishFullName = "Republic of Côte d'Ivoire", NativeFullName = "République de Côte d'Ivoire",
                LanguageEnglish = "French", LanguageNative = "Français",
                SearchKeys = new() { "кот-д'ивуар", "кот-д'івуар", "берег слоновой кости" }
            },
            new()
            {
                Alpha2 = "gh", Alpha3 = "gha",
                EnglishName = "Ghana", NativeName = "Ghana",
                EnglishFullName = "Republic of Ghana", NativeFullName = "Republic of Ghana",
                LanguageEnglish = "English", LanguageNative = "English",
                SearchKeys = new() { "гана", "twi", "akan" }
            },
            new()
            {
                Alpha2 = "gt", Alpha3 = "gtm",
                EnglishName = "Guatemala", NativeName = "Guatemala",
                EnglishFullName = "Republic of Guatemala", NativeFullName = "República de Guatemala",
                LanguageEnglish = "Spanish", LanguageNative = "Español",
                SearchKeys = new() { "гватемала", "гватэмала" }
            },

            // СНГ
            new()
            {
                Alpha2 = "by", Alpha3 = "blr",
                EnglishName = "Belarus", NativeName = "Беларусь",
                EnglishFullName = "Republic of Belarus", NativeFullName = "Рэспубліка Беларусь",
                LanguageEnglish = "Belarusian", LanguageNative = "Беларуская",
                SearchKeys = new() { "беларусь", "белоруссия", "беларускі", "белорусский", "рб", "belarus" }
            },
            new()
            {
                Alpha2 = "am", Alpha3 = "arm",
                EnglishName = "Armenia", NativeName = "Հայաստան",
                EnglishFullName = "Republic of Armenia", NativeFullName = "Հայաստանի Հանրապետություն",
                LanguageEnglish = "Armenian", LanguageNative = "Հայերեն",
                SearchKeys = new() { "армения", "арменія", "армянский", "hayastan" }
            },
            new()
            {
                Alpha2 = "az", Alpha3 = "aze",
                EnglishName = "Azerbaijan", NativeName = "Azərbaycan",
                EnglishFullName = "Republic of Azerbaijan", NativeFullName = "Azərbaycan Respublikası",
                LanguageEnglish = "Azerbaijani", LanguageNative = "Azərbaycanca",
                SearchKeys = new() { "азербайджан", "азербайджанский", "azeri" }
            },
            new()
            {
                Alpha2 = "kg", Alpha3 = "kgz",
                EnglishName = "Kyrgyzstan", NativeName = "Кыргызстан",
                EnglishFullName = "Kyrgyz Republic", NativeFullName = "Кыргыз Республикасы",
                LanguageEnglish = "Kyrgyz", LanguageNative = "Кыргызча",
                SearchKeys = new() { "киргизия", "кыргызстан", "киргизстан", "киргизский", "кыргызский" }
            },
            new()
            {
                Alpha2 = "tj", Alpha3 = "tjk",
                EnglishName = "Tajikistan", NativeName = "Тоҷикистон",
                EnglishFullName = "Republic of Tajikistan", NativeFullName = "Ҷумҳурии Тоҷикистон",
                LanguageEnglish = "Tajik", LanguageNative = "Тоҷикӣ",
                SearchKeys = new() { "таджикистан", "таджикский", "тоҷикӣ" }
            },
            new()
            {
                Alpha2 = "md", Alpha3 = "mda",
                EnglishName = "Moldova", NativeName = "Moldova",
                EnglishFullName = "Republic of Moldova", NativeFullName = "Republica Moldova",
                LanguageEnglish = "Romanian", LanguageNative = "Română",
                SearchKeys = new() { "молдова", "молдавия", "молдавский" }
            },
            new()
            {
                Alpha2 = "tm", Alpha3 = "tkm",
                EnglishName = "Turkmenistan", NativeName = "Türkmenistan",
                EnglishFullName = "Turkmenistan", NativeFullName = "Türkmenistan",
                LanguageEnglish = "Turkmen", LanguageNative = "Türkmençe",
                SearchKeys = new() { "туркменистан", "туркмения", "туркменский", "türkmen" }
            }
        };

            SearchIndex = new Dictionary<string, CountryData>(StringComparer.Ordinal);
            BuildSearchIndex();
        }

        private static void BuildSearchIndex()
        {
            foreach (var country in Countries)
            {
                AddKey(country.Alpha2, country);
                AddKey(country.Alpha3, country);
                AddKey(country.EnglishName, country);
                AddKey(country.NativeName, country);
                AddKey(country.EnglishFullName, country);
                AddKey(country.NativeFullName, country);
                AddKey(country.LanguageEnglish, country);
                AddKey(country.LanguageNative, country);

                foreach (var key in country.SearchKeys)
                    AddKey(key, country);
            }
        }

        private static void AddKey(string? key, CountryData country)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            string normalized = key.Trim().ToLowerInvariant();
            if (normalized.Length == 0)
                return;

            // Первое вхождение побеждает при коллизиях (English, Español, العربية…)
            SearchIndex.TryAdd(normalized, country);
        }

        /// <summary>Найти страну и вернуть нужное поле.</summary>
        public static string? ConvertCountry(string input, OutputFormat format)
        {
            var country = Find(input);
            if (country is null)
                return null;

            return format switch
            {
                OutputFormat.Alpha2 => country.Alpha2,
                OutputFormat.Alpha3 => country.Alpha3,
                OutputFormat.EnglishName => country.EnglishName,
                OutputFormat.NativeName => country.NativeName,
                OutputFormat.EnglishFullName => country.EnglishFullName ?? country.EnglishName,
                OutputFormat.NativeFullName => country.NativeFullName ?? country.NativeName,
                OutputFormat.LanguageEnglish => country.LanguageEnglish,
                OutputFormat.LanguageNative => country.LanguageNative,
                _ => null
            };
        }

        /// <summary>Найти страну по любому известному ключу.</summary>
        public static CountryData? Find(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            SearchIndex.TryGetValue(input.Trim().ToLowerInvariant(), out var country);
            return country;
        }

        /// <summary>Все страны, у которых язык совпадает (en или native), без учёта коллизий индекса.</summary>
        public static List<CountryData> FindAllByLanguage(string language)
        {
            var result = new List<CountryData>();
            if (string.IsNullOrWhiteSpace(language))
                return result;

            string normalized = language.Trim().ToLowerInvariant();

            foreach (var country in Countries)
            {
                if (string.Equals(country.LanguageEnglish, normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(country.LanguageNative, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(country);
                }
            }

            return result;
        }

        public static IReadOnlyList<CountryData> GetAll() => Countries;
    }
    #endregion
}
