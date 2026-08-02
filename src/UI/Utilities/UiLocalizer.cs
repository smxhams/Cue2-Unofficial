using System;
using Godot;

namespace Cue2.UI.Utilities;

/// <summary>
/// Applies Godot <see cref="TranslationServer"/> messages to control trees.
/// </summary>
/// <remarks>
/// English source strings are the catalog keys (stored in node metadata on first pass)
/// so locale switches never lose the original msgid. Dynamic user content (LineEdit text,
/// session titles, etc.) is not overwritten — only labels, button captions, tooltips,
/// placeholders, and window titles are localized.
/// <para/>
/// Automation: extract scene/code strings into <c>translations/cue2.csv</c> (see
/// <c>tools/i18n/extract_ui_strings.py</c>), translate columns, then call
/// <see cref="LocalizeTree"/> from UI <c>_Ready</c> and on <c>LocaleChanged</c>.
/// </remarks>
public static class UiLocalizer
{
	/// <summary>Metadata key for original (English) control text.</summary>
	public const string MetaText = "tr_src_text";

	/// <summary>Metadata key for original tooltip text.</summary>
	public const string MetaTooltip = "tr_src_tooltip";

	/// <summary>Metadata key for original placeholder text.</summary>
	public const string MetaPlaceholder = "tr_src_placeholder";

	/// <summary>Metadata key for original window title.</summary>
	public const string MetaTitle = "tr_src_title";

	/// <summary>
	/// When present and true, this node (not necessarily children) is skipped.
	/// </summary>
	public const string MetaSkip = "tr_skip";

	/// <summary>
	/// Stable English menu identity for Settings tree items (display text may be translated).
	/// </summary>
	public const string MetaMenuKey = "menu_key";

	/// <summary>
	/// Looks up a message key in the current locale (falls back to the key / English catalog).
	/// </summary>
	/// <param name="messageKey">Catalog key (usually the English source string).</param>
	/// <returns>Translated string, or the key when missing.</returns>
	public static string T(string messageKey)
	{
		if (string.IsNullOrEmpty(messageKey))
			return messageKey ?? string.Empty;
		return TranslationServer.Translate(messageKey);
	}

	/// <summary>
	/// Formats a translated template with <see cref="string.Format(string, object[])"/>.
	/// </summary>
	/// <param name="messageKey">Catalog key containing <c>{0}</c>-style placeholders.</param>
	/// <param name="args">Format arguments.</param>
	/// <returns>Formatted translated string.</returns>
	public static string Tf(string messageKey, params object[] args)
	{
		string fmt = T(messageKey);
		if (args == null || args.Length == 0)
			return fmt;
		try
		{
			return string.Format(fmt, args);
		}
		catch (FormatException)
		{
			return fmt;
		}
	}

	/// <summary>
	/// Recursively localizes a node and its descendants.
	/// </summary>
	/// <param name="root">Root node (scene root or panel).</param>
	/// <param name="includeRoot">When true, also localize <paramref name="root"/> itself.</param>
	public static void LocalizeTree(Node root, bool includeRoot = true)
	{
		if (root == null || !GodotObject.IsInstanceValid(root))
			return;

		if (includeRoot)
			LocalizeNode(root);

		foreach (Node child in root.GetChildren())
			LocalizeTree(child, includeRoot: true);
	}

	/// <summary>
	/// Localizes a single node when it has user-facing static text properties.
	/// </summary>
	/// <param name="node">Node to localize.</param>
	public static void LocalizeNode(Node node)
	{
		if (node == null || !GodotObject.IsInstanceValid(node))
			return;

		if (node.HasMeta(MetaSkip) && node.GetMeta(MetaSkip).AsBool())
			return;

		// More-derived Button types first (CheckBox/OptionButton inherit Button in Godot).
		switch (node)
		{
			case Window window:
				LocalizeWindowTitle(window);
				break;
			case Label label:
				LocalizeTextControl(label, () => label.Text, v => label.Text = v);
				LocalizeTooltip(label);
				break;
			case LinkButton linkButton:
				LocalizeTextControl(linkButton, () => linkButton.Text, v => linkButton.Text = v);
				LocalizeTooltip(linkButton);
				break;
			case OptionButton optionButton:
				// Option items are often dynamic; only tooltips are safe here.
				LocalizeTooltip(optionButton);
				break;
			case CheckBox checkBox:
				LocalizeTextControl(checkBox, () => checkBox.Text, v => checkBox.Text = v);
				LocalizeTooltip(checkBox);
				break;
			case CheckButton checkButton:
				LocalizeTextControl(checkButton, () => checkButton.Text, v => checkButton.Text = v);
				LocalizeTooltip(checkButton);
				break;
			case Button button:
				// Empty Text is common for icon-only buttons; still localize tooltip.
				if (!string.IsNullOrEmpty(button.Text) || button.HasMeta(MetaText))
					LocalizeTextControl(button, () => button.Text, v => button.Text = v);
				LocalizeTooltip(button);
				break;
			case LineEdit lineEdit:
				// Do not overwrite user-entered Text — only placeholder + tooltip.
				LocalizePlaceholder(lineEdit);
				LocalizeTooltip(lineEdit);
				break;
			case TextEdit textEdit:
				LocalizePlaceholder(textEdit);
				LocalizeTooltip(textEdit);
				break;
			case SpinBox spinBox:
				LocalizeTooltip(spinBox);
				break;
			case ProgressBar progressBar:
				LocalizeTooltip(progressBar);
				break;
			case Godot.Range range:
				// Slider / other Range controls — tooltips only.
				LocalizeTooltip(range);
				break;
			case Control control:
				// Generic controls may only have tooltips (e.g. resize grips).
				LocalizeTooltip(control);
				break;
		}
	}

	/// <summary>
	/// Sets a Settings tree item's display text from a stable English menu key.
	/// </summary>
	/// <param name="item">Tree item.</param>
	/// <param name="column">Column index.</param>
	/// <param name="englishKey">Stable English label used for navigation / persistence.</param>
	public static void SetTreeItemText(TreeItem item, int column, string englishKey)
	{
		if (item == null || string.IsNullOrEmpty(englishKey))
			return;
		item.SetMeta(MetaMenuKey, englishKey);
		item.SetText(column, T(englishKey));
	}

	/// <summary>
	/// Sets a Settings tree item tooltip from an English source string.
	/// </summary>
	/// <param name="item">Tree item.</param>
	/// <param name="column">Column index.</param>
	/// <param name="englishTooltip">English tooltip (catalog key).</param>
	public static void SetTreeItemTooltip(TreeItem item, int column, string englishTooltip)
	{
		if (item == null || string.IsNullOrEmpty(englishTooltip))
			return;
		item.SetMeta(MetaTooltip + column, englishTooltip);
		item.SetTooltipText(column, T(englishTooltip));
	}

	/// <summary>
	/// Returns the stable English menu key for a tree item (falls back to column-0 text).
	/// </summary>
	/// <param name="item">Tree item.</param>
	/// <returns>English menu key.</returns>
	public static string GetTreeItemMenuKey(TreeItem item)
	{
		if (item == null)
			return string.Empty;
		if (item.HasMeta(MetaMenuKey))
			return item.GetMeta(MetaMenuKey).AsString();
		return item.GetText(0) ?? string.Empty;
	}

	/// <summary>
	/// Re-applies translations to a tree item that was configured with <see cref="SetTreeItemText"/>.
	/// </summary>
	/// <param name="item">Root item to walk (depth-first).</param>
	/// <param name="column">Column index.</param>
	public static void RelocalizeTreeItems(TreeItem item, int column = 0)
	{
		if (item == null)
			return;

		if (item.HasMeta(MetaMenuKey))
		{
			string key = item.GetMeta(MetaMenuKey).AsString();
			item.SetText(column, T(key));
		}

		string tipMeta = MetaTooltip + column;
		if (item.HasMeta(tipMeta))
		{
			string tipKey = item.GetMeta(tipMeta).AsString();
			item.SetTooltipText(column, T(tipKey));
		}

		TreeItem child = item.GetFirstChild();
		while (child != null)
		{
			RelocalizeTreeItems(child, column);
			child = child.GetNext();
		}
	}

	private static void LocalizeWindowTitle(Window window)
	{
		string src = CaptureOrGet(window, MetaTitle, window.Title);
		if (!string.IsNullOrEmpty(src))
			window.Title = T(src);
	}

	private static void LocalizeTextControl(Control control, Func<string> getText, Action<string> setText)
	{
		string current = getText() ?? string.Empty;
		// Icon-only or intentionally blank captions: only keep meta if already set.
		if (string.IsNullOrEmpty(current) && !control.HasMeta(MetaText))
			return;

		string src = CaptureOrGet(control, MetaText, current);
		if (!string.IsNullOrEmpty(src))
			setText(T(src));
	}

	private static void LocalizeTooltip(Control control)
	{
		string current = control.TooltipText ?? string.Empty;
		if (string.IsNullOrEmpty(current) && !control.HasMeta(MetaTooltip))
			return;

		string src = CaptureOrGet(control, MetaTooltip, current);
		if (!string.IsNullOrEmpty(src))
			control.TooltipText = T(src);
	}

	private static void LocalizePlaceholder(LineEdit lineEdit)
	{
		string current = lineEdit.PlaceholderText ?? string.Empty;
		if (string.IsNullOrEmpty(current) && !lineEdit.HasMeta(MetaPlaceholder))
			return;
		string src = CaptureOrGet(lineEdit, MetaPlaceholder, current);
		if (!string.IsNullOrEmpty(src))
			lineEdit.PlaceholderText = T(src);
	}

	private static void LocalizePlaceholder(TextEdit textEdit)
	{
		string current = textEdit.PlaceholderText ?? string.Empty;
		if (string.IsNullOrEmpty(current) && !textEdit.HasMeta(MetaPlaceholder))
			return;
		string src = CaptureOrGet(textEdit, MetaPlaceholder, current);
		if (!string.IsNullOrEmpty(src))
			textEdit.PlaceholderText = T(src);
	}

	/// <summary>
	/// Returns existing source metadata, or stores <paramref name="current"/> as the source on first use.
	/// </summary>
	private static string CaptureOrGet(GodotObject obj, string metaKey, string current)
	{
		if (obj.HasMeta(metaKey))
		{
			string existing = obj.GetMeta(metaKey).AsString();
			if (!string.IsNullOrEmpty(existing))
				return existing;
		}

		if (string.IsNullOrEmpty(current))
			return string.Empty;

		obj.SetMeta(metaKey, current);
		return current;
	}
}
