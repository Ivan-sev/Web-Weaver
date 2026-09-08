using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace WebWeaver
{
    internal static class BtnModel
    {

        // "Название|Описание|IdButton|(True) если скрыть описание"
        public class BtnData
        {
            public required string Title { get; set; }
            public required string[] Button { get; set; }
        }

        public static BtnData GetData(FrameworkElement element)
        {
            return element.Name switch
            {
                "File" => File(),
                "Tools" => Tools(),
                "View" => View(),
                _ => Null()
            };
        }

        private static BtnData File()
        {
            return new BtnData
            {
                Title = "Файлы",
                Button =
                [
                    "＋ Новая нода|Создать новую ноду (Insert)|BtnNewNode|True",
                    "💾 Сохранить|Сохранить карту (Ctrl+S)|BtnSave|True",
                    "📂 Открыть|Открыть карту (Ctrl+O)|BtnOpen|True",
                    "🌳 Дерево|Дерево нод: кто к кому принадлежит|ShowNodeTree|True"
                ]
            };
        }

        private static BtnData Tools()
        {
            return new BtnData
            {
                Title = "Инструменты",
                Button =
                [
                    "⏳ История|История изменений|BtnHistory|True",
                    "🗑 Очистить всё|Очистить всё (Ctrl+Del)|BtnClearAll|True",
                    "🔍 Поиск|Поиск нод в проекте (Ctrl+F)|BtnFindNode|True",
                    "⚙️ Настройки|Настройки приложения|BtnSettings|True"
                ]
            };
        }

        private static BtnData View()
        {
            return new BtnData
            {
                Title = "Вид",
                Button =
                [
                    "⊡ Сброс вида|Сбросить масштаб и положение (Ctrl+Home)|BtnResetView|True",
                    "🔍＋|Приблизить (Ctrl+)|BtnZoomIn|True",
                    "🔍−|Отдалить (Ctrl-)|BtnZoomOut|True"
                ]
            };
        }

        private static BtnData Null()
        {
            return new BtnData
            {
                Title = "",
                Button =
                [
                    ""
                ]
            };
        }
    }
}
