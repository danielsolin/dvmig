using dvmig.Core.Settings;

namespace dvmig.Core.Interfaces
{
/// <summary>
    /// Service interface for loading and saving application settings.
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>
        /// Loads the user settings from persistent storage.
        /// </summary>
        /// <returns>The loaded user settings.</returns>
        UserSettings LoadSettings();

        /// <summary>
        /// Saves the specified user settings to persistent storage.
        /// </summary>
        /// <param name="settings">The settings to save.</param>
        void SaveSettings(UserSettings settings);
    }
}