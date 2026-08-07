// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace Cue2.Services;

/// <summary>
/// Application localization framework built on Godot's <see cref="TranslationServer"/>.
/// </summary>
/// <remarks>
/// Loads message catalogs from <c>res://translations/cue2.csv</c> at runtime, registers
/// each locale column as a <see cref="Translation"/>, and applies the user's preferred
/// locale from <see cref="UserDataManager"/>. Supported locales are listed in
/// <see cref="SupportedLocales"/> (extend CSV columns + this list to add languages).
/// <para/>
/// Automation: run <c>tools/i18n/extract_ui_strings.py --merge translations/cue2.csv</c>
/// to discover new scene strings, then fill empty locale cells (spreadsheet, Weblate, or LLM).
/// UI code uses English source strings as keys via <see cref="Cue2.UI.Utilities.UiLocalizer"/>.
/// </remarks>
public partial class LocalizationService : Node
{
	/// <summary>Path to the spreadsheet-style translation catalog.</summary>
	public const string CatalogPath = "res://translations/cue2.csv";

	/// <summary>Default and fallback locale code (English).</summary>
	public const string DefaultLocale = "en";

	/// <summary>Te reo Māori locale code (ISO 639-1).</summary>
	public const string LocaleMaori = "mi";

	/// <summary>Spanish locale code (ISO 639-1).</summary>
	public const string LocaleSpanish = "es";

	/// <summary>
	/// Supported UI locales: ISO-style code and native display name.
	/// Display names stay in their own language so the picker remains usable in any locale.
	/// </summary>
	/// <remarks>
	/// Order: English (default), te reo Māori (pipeline / Aotearoa test), Spanish
	/// </remarks>
	public static readonly IReadOnlyList<(string Code, string DisplayName)> SupportedLocales =
		new List<(string, string)>
		{
			(DefaultLocale, "English"),
			(LocaleMaori, "Te reo Māori"),
			(LocaleSpanish, "Español"),
		};

	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	private bool _initialized;
	private readonly HashSet<string> _registeredLocales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Loads the CSV catalog, registers translations, and applies the saved user locale.
	/// Safe to call once after <see cref="UserDataManager"/> has loaded preferences.
	/// </summary>
	public void Initialize()
	{
		if (_initialized)
			return;

		_globalData = GetNodeOrNull<GlobalData>("/root/GlobalData");
		_globalSignals = GetNodeOrNull<GlobalSignals>("/root/GlobalSignals");

		try
		{
			// Fallback locale is configured in project.godot [internationalization] locale/fallback.
			LoadAndRegisterCatalog();

			// Ensure English is always registered even if the CSV is missing or incomplete.
			EnsureLocaleRegistered(DefaultLocale);

			string preferred = _globalData?.UserDataManager?.Locale ?? DefaultLocale;
			ApplyLocale(preferred, emitSignal: false);

			_initialized = true;
			GD.Print($"LocalizationService:Initialize - Ready. Locale={TranslationServer.GetLocale()} " +
			         $"registered=[{string.Join(", ", _registeredLocales)}]");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"LocalizationService:Initialize - Failed: {ex.Message}");
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
				$"Localization init failed: {ex.Message}", 2);
			try
			{
				EnsureLocaleRegistered(DefaultLocale);
				TranslationServer.SetLocale(DefaultLocale);
			}
			catch
			{
				// Best-effort fallback only.
			}
			_initialized = true;
		}
	}

	/// <summary>
	/// Whether <paramref name="localeCode"/> is in the supported locale list.
	/// </summary>
	/// <param name="localeCode">ISO-style locale code (e.g. <c>en</c>).</param>
	/// <returns>True if the locale is supported for the UI language picker.</returns>
	public bool IsSupported(string localeCode)
	{
		if (string.IsNullOrWhiteSpace(localeCode))
			return false;

		foreach (var (code, _) in SupportedLocales)
		{
			if (string.Equals(code, localeCode, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	/// <summary>
	/// Resolves a locale code to a supported value, falling back to English.
	/// </summary>
	/// <param name="localeCode">Requested locale code.</param>
	/// <returns>A supported locale code.</returns>
	public string ResolveLocale(string localeCode)
	{
		if (IsSupported(localeCode))
			return localeCode.Trim().ToLowerInvariant();
		return DefaultLocale;
	}

	/// <summary>
	/// Native display name for a locale code (e.g. <c>en</c> → <c>English</c>).
	/// </summary>
	/// <param name="localeCode">ISO-style locale code.</param>
	/// <returns>Display name, or the code itself if unknown.</returns>
	public string GetDisplayName(string localeCode)
	{
		if (string.IsNullOrWhiteSpace(localeCode))
			return DefaultLocale;

		foreach (var (code, name) in SupportedLocales)
		{
			if (string.Equals(code, localeCode, StringComparison.OrdinalIgnoreCase))
				return name;
		}
		return localeCode;
	}

	/// <summary>
	/// Applies a locale to <see cref="TranslationServer"/> and optionally notifies listeners.
	/// Does not persist the preference; use <see cref="SetUserLocale"/> for that.
	/// </summary>
	/// <param name="localeCode">Requested locale code.</param>
	/// <param name="emitSignal">When true, emits <see cref="GlobalSignals.LocaleChanged"/>.</param>
	/// <returns>The resolved locale that was applied.</returns>
	public string ApplyLocale(string localeCode, bool emitSignal = true)
	{
		string resolved = ResolveLocale(localeCode);
		EnsureLocaleRegistered(resolved);

		string previous = TranslationServer.GetLocale();
		TranslationServer.SetLocale(resolved);

		GD.Print($"LocalizationService:ApplyLocale - {previous} → {resolved}");

		if (emitSignal && !string.Equals(previous, resolved, StringComparison.OrdinalIgnoreCase))
			_globalSignals?.EmitSignal(nameof(GlobalSignals.LocaleChanged), resolved);

		return resolved;
	}

	/// <summary>
	/// Persists the locale preference and applies it to the translation server.
	/// </summary>
	/// <param name="localeCode">Requested locale code.</param>
	/// <returns>The resolved locale that was stored and applied.</returns>
	public string SetUserLocale(string localeCode)
	{
		string resolved = ResolveLocale(localeCode);

		if (_globalData?.UserDataManager != null)
		{
			// Property setter saves when the value changes.
			_globalData.UserDataManager.Locale = resolved;
		}

		// Always apply so TranslationServer stays in sync even if the value was already saved.
		string previous = TranslationServer.GetLocale();
		TranslationServer.SetLocale(resolved);
		EnsureLocaleRegistered(resolved);

		if (!string.Equals(previous, resolved, StringComparison.OrdinalIgnoreCase))
		{
			GD.Print($"LocalizationService:SetUserLocale - {previous} → {resolved}");
			_globalSignals?.EmitSignal(nameof(GlobalSignals.LocaleChanged), resolved);
		}
		else
		{
			GD.Print($"LocalizationService:SetUserLocale - Locale remains {resolved}");
		}

		return resolved;
	}

	/// <summary>
	/// Fills an <see cref="OptionButton"/> with supported languages and selects the given locale.
	/// Item metadata stores the locale code string.
	/// </summary>
	/// <param name="button">Target option button (cleared and rebuilt).</param>
	/// <param name="selectedLocale">Locale code to select; falls back to English if unsupported.</param>
	public void PopulateLanguageOptionButton(OptionButton button, string selectedLocale)
	{
		if (button == null || !GodotObject.IsInstanceValid(button))
			return;

		string resolved = ResolveLocale(selectedLocale);
		button.Clear();

		int selectedIndex = 0;
		for (int i = 0; i < SupportedLocales.Count; i++)
		{
			var (code, displayName) = SupportedLocales[i];
			button.AddItem(displayName, i);
			button.SetItemMetadata(i, code);
			if (string.Equals(code, resolved, StringComparison.OrdinalIgnoreCase))
				selectedIndex = i;
		}

		button.Selected = selectedIndex;
	}

	/// <summary>
	/// Reads the locale code stored as metadata on the selected option-button item.
	/// </summary>
	/// <param name="button">Language option button previously filled by <see cref="PopulateLanguageOptionButton"/>.</param>
	/// <returns>Locale code, or <see cref="DefaultLocale"/> if unavailable.</returns>
	public string GetLocaleFromOptionButton(OptionButton button)
	{
		if (button == null || !GodotObject.IsInstanceValid(button) || button.ItemCount == 0)
			return DefaultLocale;

		int index = button.Selected;
		if (index < 0 || index >= button.ItemCount)
			return DefaultLocale;

		Variant meta = button.GetItemMetadata(index);
		string code = meta.AsString();
		return ResolveLocale(code);
	}

	/// <summary>
	/// Translates a message key using the current locale (wrapper for <see cref="TranslationServer.Translate"/>).
	/// </summary>
	/// <param name="messageKey">Catalog key (e.g. <c>SETTINGS_LANGUAGE</c>).</param>
	/// <returns>Translated string, or the key if no message exists.</returns>
	public string Translate(string messageKey)
	{
		if (string.IsNullOrEmpty(messageKey))
			return string.Empty;
		return TranslationServer.Translate(messageKey);
	}

	// ── Catalog loading ───────────────────────────────────────────────────

	/// <summary>
	/// Parses <see cref="CatalogPath"/> and registers one <see cref="Translation"/> per locale column.
	/// </summary>
	private void LoadAndRegisterCatalog()
	{
		if (!Godot.FileAccess.FileExists(CatalogPath))
		{
			GD.PrintErr($"LocalizationService:LoadAndRegisterCatalog - Catalog not found: {CatalogPath}");
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
				$"Translation catalog missing: {CatalogPath}", 1);
			return;
		}

		using var file = Godot.FileAccess.Open(CatalogPath, Godot.FileAccess.ModeFlags.Read);
		if (file == null)
		{
			Error err = Godot.FileAccess.GetOpenError();
			GD.PrintErr($"LocalizationService:LoadAndRegisterCatalog - Failed to open catalog: {err}");
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
				$"Failed to open translation catalog: {err}", 2);
			return;
		}

		string text = file.GetAsText();
		if (string.IsNullOrWhiteSpace(text))
		{
			GD.PrintErr("LocalizationService:LoadAndRegisterCatalog - Catalog is empty.");
			return;
		}

		// Strip UTF-8 BOM if present. Keep real newlines — records may be multiline (quoted fields).
		if (text.Length > 0 && text[0] == '\uFEFF')
			text = text.Substring(1);

		List<List<string>> records = ParseCsvRecords(text);
		if (records.Count == 0)
			return;

		List<string> header = records[0];
		if (header.Count < 2)
		{
			GD.PrintErr("LocalizationService:LoadAndRegisterCatalog - Invalid header (need keys + at least one locale).");
			return;
		}

		// Column 0 is keys; remaining columns are locale codes (skip comment columns starting with _).
		var localeColumns = new List<(int Index, string Locale)>();
		for (int c = 1; c < header.Count; c++)
		{
			string col = (header[c] ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(col) || col.StartsWith('_') || col.StartsWith('?'))
				continue;
			localeColumns.Add((c, col.ToLowerInvariant()));
		}

		if (localeColumns.Count == 0)
		{
			GD.PrintErr("LocalizationService:LoadAndRegisterCatalog - No locale columns found.");
			return;
		}

		var translations = new Dictionary<string, Translation>(StringComparer.OrdinalIgnoreCase);
		foreach (var (_, locale) in localeColumns)
		{
			if (!translations.ContainsKey(locale))
			{
				var t = new Translation { Locale = locale };
				translations[locale] = t;
			}
		}

		int messageCount = 0;
		for (int recordIndex = 1; recordIndex < records.Count; recordIndex++)
		{
			List<string> fields = records[recordIndex];
			if (fields.Count == 0)
				continue;

			string key = fields[0]?.Trim() ?? string.Empty;
			if (string.IsNullOrEmpty(key) || key.StartsWith('#'))
				continue;

			foreach (var (colIndex, locale) in localeColumns)
			{
				if (colIndex >= fields.Count)
					continue;
				string message = fields[colIndex] ?? string.Empty;
				if (string.IsNullOrEmpty(message))
					continue;

				translations[locale].AddMessage(key, message);
				messageCount++;
			}
		}

		foreach (var kvp in translations)
		{
			// Replace any previous registration for this locale from earlier init attempts.
			TranslationServer.AddTranslation(kvp.Value);
			_registeredLocales.Add(kvp.Key);
		}

		GD.Print($"LocalizationService:LoadAndRegisterCatalog - Registered {translations.Count} locale(s), {messageCount} message cell(s) from {records.Count - 1} row(s).");
	}

	/// <summary>
	/// Registers an empty (or display-name-only) translation for a locale if none was loaded from CSV.
	/// </summary>
	/// <param name="localeCode">Locale to ensure.</param>
	private void EnsureLocaleRegistered(string localeCode)
	{
		string locale = ResolveLocale(localeCode);
		if (_registeredLocales.Contains(locale))
			return;

		var translation = new Translation { Locale = locale };
		// Seed the language display name so Tr("LOCALE_NAME_en") works even without CSV.
		translation.AddMessage($"LOCALE_NAME_{locale}", GetDisplayName(locale));
		TranslationServer.AddTranslation(translation);
		_registeredLocales.Add(locale);
		GD.Print($"LocalizationService:EnsureLocaleRegistered - Registered fallback catalog for '{locale}'.");
	}

	/// <summary>
	/// Parses a full CSV document into records, supporting multiline quoted fields and <c>""</c> escapes.
	/// </summary>
	/// <param name="text">Entire CSV file contents.</param>
	/// <returns>List of field lists (never null).</returns>
	/// <remarks>
	/// Line-splitting first is incorrect for Godot-style translation spreadsheets that embed
	/// newlines inside quoted cells (e.g. welcome body). This parser walks the full text so
	/// those records stay intact.
	/// </remarks>
	private static List<List<string>> ParseCsvRecords(string text)
	{
		var records = new List<List<string>>();
		if (string.IsNullOrEmpty(text))
			return records;

		var fields = new List<string>();
		var current = new StringBuilder();
		bool inQuotes = false;

		void EndField()
		{
			fields.Add(current.ToString());
			current.Clear();
		}

		void EndRecord()
		{
			EndField();
			// Skip completely empty rows (e.g. trailing newline).
			bool any = false;
			foreach (string f in fields)
			{
				if (!string.IsNullOrWhiteSpace(f))
				{
					any = true;
					break;
				}
			}
			if (any)
				records.Add(fields);
			fields = new List<string>();
		}

		for (int i = 0; i < text.Length; i++)
		{
			char ch = text[i];
			if (inQuotes)
			{
				if (ch == '"')
				{
					// Escaped quote ""
					if (i + 1 < text.Length && text[i + 1] == '"')
					{
						current.Append('"');
						i++;
					}
					else
					{
						inQuotes = false;
					}
				}
				else
				{
					// Preserve newlines inside quoted fields.
					current.Append(ch);
				}
			}
			else
			{
				if (ch == '"')
				{
					inQuotes = true;
				}
				else if (ch == ',')
				{
					EndField();
				}
				else if (ch == '\n')
				{
					EndRecord();
				}
				else if (ch == '\r')
				{
					// Normalize CRLF / bare CR as record separators outside quotes.
					if (i + 1 < text.Length && text[i + 1] == '\n')
						i++;
					EndRecord();
				}
				else
				{
					current.Append(ch);
				}
			}
		}

		// Final field/record when file does not end with a newline.
		if (current.Length > 0 || fields.Count > 0)
			EndRecord();

		return records;
	}
}
