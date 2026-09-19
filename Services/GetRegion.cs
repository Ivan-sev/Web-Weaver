using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WebWeaver.Services
{
    public class Region
    {
        public static string GetSystemRegionFromRegistry()
        {
            try
            {
                // Путь к настройкам географического положения пользователя
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\International\Geo"))
                {
                    if (key != null)
                    {
                        // Сначала пробуем прочитать код страны напрямую (например, "RU")
                        object nationName = key.GetValue("Name");
                        if (nationName != null && !string.IsNullOrWhiteSpace(nationName.ToString()))
                        {
                            return nationName.ToString();
                        }

                        // Если "Name" пустой, читаем старый числовой GeoID (например, 203 для России)
                        object geoIdObj = key.GetValue("Nation");
                        if (geoIdObj != null && int.TryParse(geoIdObj.ToString(), out int geoId))
                        {
                            // Конвертируем числовой ID в понятный ISO-код через класс RegionInfo
                            return new RegionInfo(geoId).TwoLetterISORegionName;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Логируем ошибку, если у приложения нет прав доступа к реестру
                Console.WriteLine($"Ошибка чтения реестра: {ex.Message}");
            }

            // Резервный вариант: если реестр недоступен, берем настройки текущего потока .NET
            return RegionInfo.CurrentRegion.TwoLetterISORegionName;
        }

        /// <summary>
        /// Возвращает полное название страны на её собственном официальном языке.
        /// </summary>
        /// <param name="twoLetterCode">Двухбуквенный ISO-код страны (например, "RU", "US", "DE")</param>
        /// <returns>Полное название страны или исходный код в случае ошибки</returns>
        public static string GetNativeName(string twoLetterCode)
        {
            if (string.IsNullOrWhiteSpace(twoLetterCode) || twoLetterCode.Length != 2)
            {
                return "Неверный код";
            }

            try
            {
                // Переводим код в верхний регистр (RU вместо ru)
                string isoCode = twoLetterCode.ToUpperInvariant();

                // Создаем культуру, используя код страны как язык по умолчанию
                // Для "RU" это будет "ru-RU", для "US" -> "en-US"
                CultureInfo culture = CultureInfo.CreateSpecificCulture(isoCode);

                // Получаем информацию о регионе на основе созданной культуры
                RegionInfo region = new RegionInfo(culture.Name);

                // NativeName возвращает название страны на языке этой локали
                return region.NativeName;
            }
            catch (ArgumentException)
            {
                // Сработает, если передан несуществующий ISO-код
                return $"Неизвестная страна ({twoLetterCode})";
            }
            catch (Exception)
            {
                return twoLetterCode;
            }
        }

        /// <summary>
        /// Возвращает название основного языка страны по её двухбуквенному коду.
        /// </summary>
        /// <param name="twoLetterCountryCode">ISO-код страны (например, "RU", "US", "DE")</param>
        /// <param name="inRussian">true — вернуть на русском, false — на родном языке страны</param>
        public static string GetLanguageByCountry(string twoLetterCountryCode, bool inRussian = true)
        {
            if (string.IsNullOrWhiteSpace(twoLetterCountryCode) || twoLetterCountryCode.Length != 2)
            {
                return "Неверный код страны";
            }

            try
            {
                string countryIso = twoLetterCountryCode.ToUpperInvariant();

                // Создаем специфичную культуру (например, для "RU" -> "ru-RU", для "US" -> "en-US")
                CultureInfo culture = CultureInfo.CreateSpecificCulture(countryIso);

                if (inRussian)
                {
                    // DisplayName возвращает название языка на языке текущей системы (в вашем случае — на русском)
                    // Отрезаем часть с регионом в скобках, оставляя только язык (например, из "русский (Россия)" делаем "русский")
                    string fullName = culture.DisplayName;
                    int bracketIndex = fullName.IndexOf(" (");
                    return bracketIndex > 0 ? fullName.Substring(0, bracketIndex) : fullName;
                }
                else
                {
                    // NativeName возвращает название языка на нем самом (например, "English", "Deutsch")
                    string nativeName = culture.NativeName;
                    int bracketIndex = nativeName.IndexOf(" (");
                    return bracketIndex > 0 ? nativeName.Substring(0, bracketIndex) : nativeName;
                }
            }
            catch (ArgumentException)
            {
                return $"Неизвестный язык ({twoLetterCountryCode})";
            }
            catch (Exception)
            {
                return "Ошибка определения языка";
            }
        }
    }
}
