using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        /// <summary>Session buffer for Copy Tags / Paste Tags (user tags only). Also mirrored to system clipboard as newline list.</summary>
        private List<string> _userTagClipboardTags;
        private int _userTagClipboardSourceItemCount;

        private const int UserTagClipboardReplaceConfirmMinTargets = 20;

        private bool UserTagClipboardHasTags()
        {
            return _userTagClipboardTags != null && _userTagClipboardTags.Count > 0;
        }

        private int UserTagClipboardTagCount()
        {
            return _userTagClipboardTags != null ? _userTagClipboardTags.Count : 0;
        }

        /// <summary>Copy user tags from current selection into session buffer + system clipboard (union across items).</summary>
        private void UserTagClipboardCopyFromSelection()
        {
            if (selectedFiles == null || selectedFiles.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.none_selected", "Nothing selected."), 1.5f);
                return;
            }

            var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int sourceItems = 0;
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry fe = selectedFiles[i];
                if (fe == null || fe is InternalSettingRowEntry) continue;
                HashSet<string> row = DetailStripCollectUserTags(fe);
                if (row == null || row.Count == 0) continue;
                sourceItems++;
                foreach (string t in row)
                {
                    if (!string.IsNullOrEmpty(t)) union.Add(t);
                }
            }

            if (union.Count == 0)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.detail.tags_copy_empty", "No tags on this item."), 1.8f);
                return;
            }

            var list = new List<string>(union.Count);
            foreach (string t in union)
                list.Add(t);
            list.Sort(StringComparer.OrdinalIgnoreCase);

            _userTagClipboardTags = list;
            _userTagClipboardSourceItemCount = Math.Max(1, sourceItems);

            try
            {
                var sb = new StringBuilder(list.Count * 16);
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append('\n');
                    sb.Append(list[i]);
                }
                GUIUtility.systemCopyBuffer = sb.ToString();
            }
            catch { }

            try { DetailStripSyncTagClipboardActionChrome(); } catch { }

            if (_userTagClipboardSourceItemCount > 1)
            {
                ShowTemporaryStatus(string.Format(
                    VPBTranslation.T(
                        "gallery.detail.tags_copied_multi_fmt",
                        "Copied {0} tags from {1} items"),
                    list.Count, _userTagClipboardSourceItemCount), 2f);
            }
            else
            {
                ShowTemporaryStatus(string.Format(
                    VPBTranslation.T("gallery.detail.tags_copied_fmt", "Copied {0} tags"),
                    list.Count), 1.8f);
            }
        }

        /// <summary>
        /// Paste session buffer (or system clipboard tag list) onto selection.
        /// Default merge; replace=true removes target user tags not in buffer then applies buffer.
        /// </summary>
        private void UserTagClipboardPasteToSelection(bool replace)
        {
            if (selectedFiles == null || selectedFiles.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.none_selected", "Nothing selected."), 1.5f);
                return;
            }

            List<string> tags = UserTagClipboardResolvePasteTags();
            if (tags == null || tags.Count == 0)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.detail.tags_paste_empty", "No tags copied yet."), 1.8f);
                return;
            }

            List<FileEntry> targets = NormalizeUserTagMutationTargets(selectedFiles);
            if (targets.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.none_selected", "Nothing selected."), 1.5f);
                return;
            }

            if (replace && UserTagClipboardShouldConfirmReplace(targets.Count))
            {
                string title = VPBTranslation.T("gallery.detail.tags_replace_confirm_title", "Replace tags?");
                string msg = string.Format(
                    VPBTranslation.T(
                        "gallery.detail.tags_replace_confirm_body_fmt",
                        "Replace user tags on {0} item(s) with {1} copied tag(s)?\n\nExisting tags not in the clipboard will be removed."),
                    targets.Count, tags.Count);
                if (_userTagInheritVarToChildren)
                {
                    msg += "\n\n" + VPBTranslation.T(
                        "gallery.detail.tags_replace_confirm_inherit",
                        "Inherit is ON — child items inside selected VAR packages may also change.");
                }
                DisplayConfirm(title, msg, () => UserTagClipboardBeginPaste(tags, targets, replace: true));
                return;
            }

            UserTagClipboardBeginPaste(tags, targets, replace);
        }

        private bool UserTagClipboardShouldConfirmReplace(int targetCount)
        {
            if (_userTagInheritVarToChildren) return true;
            return targetCount >= UserTagClipboardReplaceConfirmMinTargets;
        }

        private void UserTagClipboardBeginPaste(List<string> tags, List<FileEntry> targets, bool replace)
        {
            if (tags == null || tags.Count == 0 || targets == null || targets.Count == 0) return;
            StartCoroutine(UserTagClipboardPasteCoroutine(new List<string>(tags), targets, replace));
        }

        /// <summary>Merge tags from first selected item onto the rest of the selection (same-view speed path).</summary>
        private void UserTagClipboardStampFromFirst()
        {
            if (selectedFiles == null || selectedFiles.Count < 2)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.detail.tags_stamp_need_multi", "Select source + targets (2+ items)."), 1.8f);
                return;
            }

            FileEntry source = null;
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry fe = selectedFiles[i];
                if (fe == null || fe is InternalSettingRowEntry) continue;
                source = fe;
                break;
            }
            if (source == null)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.none_selected", "Nothing selected."), 1.5f);
                return;
            }

            HashSet<string> srcTags = DetailStripCollectUserTags(source);
            if (srcTags == null || srcTags.Count == 0)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.detail.tags_copy_empty", "No tags on this item."), 1.8f);
                return;
            }

            var list = new List<string>(srcTags.Count);
            foreach (string t in srcTags)
            {
                if (!string.IsNullOrEmpty(t)) list.Add(t);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            if (list.Count == 0)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.detail.tags_copy_empty", "No tags on this item."), 1.8f);
                return;
            }

            // Merge onto whole selection (first already has them — idempotent).
            // Bulk apply coroutine owns success status ("Updated N item(s).").
            ApplyUserTagsToFileEntries(list, selectedFiles, remove: false);
        }

        private List<string> UserTagClipboardResolvePasteTags()
        {
            if (UserTagClipboardHasTags())
                return new List<string>(_userTagClipboardTags);

            // Fall back to system clipboard (newline list) — recognition over recall for external pastes.
            string raw = null;
            try { raw = GUIUtility.systemCopyBuffer; } catch { raw = null; }
            if (string.IsNullOrEmpty(raw)) return null;

            List<string> lines = GalleryUserTagEditorText.ParseMultilineTags(raw);
            if (lines == null || lines.Count == 0) return null;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                string n = VpbLocalDatabase.NormalizeGalleryUserTagName(lines[i]);
                if (string.IsNullOrEmpty(n) || !seen.Add(n)) continue;
                list.Add(n);
            }
            if (list.Count == 0) return null;

            // Adopt into session buffer so Paste chrome enables and next paste is instant.
            _userTagClipboardTags = list;
            _userTagClipboardSourceItemCount = 0;
            try { DetailStripSyncTagClipboardActionChrome(); } catch { }
            return new List<string>(list);
        }

        private IEnumerator UserTagClipboardPasteCoroutine(List<string> tags, List<FileEntry> targets, bool replace)
        {
            if (tags == null || tags.Count == 0) yield break;
            if (targets == null || targets.Count == 0) yield break;

            string cat = currentCategoryTitle ?? (titleText != null ? titleText.text : "");
            if (string.IsNullOrEmpty(cat)) yield break;

            bool inherit = !string.IsNullOrEmpty(cat)
                && VpbLocalDatabase.IsGalleryAllVarPseudoCategory(cat)
                && _userTagInheritVarToChildren;

            List<VpbLocalDatabase.GalleryUserTagRowKey> rows;
            if (inherit)
            {
                var pkgUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < targets.Count; i++)
                {
                    if (!TryGetGalleryRowKeysForUserTags(targets[i], out string pkg, out _)) continue;
                    if (!string.IsNullOrEmpty(pkg)) pkgUids.Add(pkg);
                }
                rows = new List<VpbLocalDatabase.GalleryUserTagRowKey>(4096);
                foreach (string pu in pkgUids)
                {
                    var catMem = new List<KeyValuePair<string, string>>(256);
                    if (!VpbLocalDatabase.TryReadCatMemRowsForPackage(pu, catMem)) continue;
                    for (int i = 0; i < catMem.Count; i++)
                    {
                        var kv = catMem[i];
                        rows.Add(new VpbLocalDatabase.GalleryUserTagRowKey
                        {
                            Category = kv.Key ?? "",
                            PkgUid = pu ?? "",
                            InternalPath = kv.Value ?? ""
                        });
                    }
                }
            }
            else
            {
                rows = new List<VpbLocalDatabase.GalleryUserTagRowKey>(targets.Count);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < targets.Count; i++)
                {
                    FileEntry fe = targets[i];
                    if (!TryGetGalleryRowKeysForUserTags(fe, out string pkg, out string ip)) continue;
                    if (string.IsNullOrEmpty(pkg)) continue;
                    string rk = (pkg ?? "") + "\n" + (ip ?? "");
                    if (!seen.Add(rk)) continue;
                    rows.Add(new VpbLocalDatabase.GalleryUserTagRowKey
                    {
                        Category = cat ?? "",
                        PkgUid = pkg ?? "",
                        InternalPath = ip ?? ""
                    });
                }
            }

            if (rows.Count == 0) yield break;

            // For replace: compute tags present on selection that are not in the clipboard (union extras).
            List<string> removeExtras = null;
            if (replace)
            {
                var keep = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
                var extras = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < targets.Count; i++)
                {
                    HashSet<string> row = DetailStripCollectUserTags(targets[i]);
                    if (row == null) continue;
                    foreach (string t in row)
                    {
                        if (string.IsNullOrEmpty(t) || keep.Contains(t)) continue;
                        extras.Add(t);
                    }
                }
                if (extras.Count > 0)
                {
                    removeExtras = new List<string>(extras.Count);
                    foreach (string t in extras)
                        removeExtras.Add(t);
                }
            }

            int[] done = new int[1];
            int[] touchedOut = new int[1];
            int[] ok = new int[1];
            List<string> removeSnap = removeExtras;
            List<string> tagsSnap = tags;
            List<VpbLocalDatabase.GalleryUserTagRowKey> rowsSnap = rows;

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int touched = 0;
                    bool success = true;
                    if (removeSnap != null && removeSnap.Count > 0)
                    {
                        int remTouched;
                        success = VpbLocalDatabase.TryRemoveGalleryUserTagsFromManyRows(rowsSnap, removeSnap, out remTouched);
                        if (success) touched = Math.Max(touched, remTouched);
                    }
                    if (success)
                    {
                        int addTouched;
                        success = VpbLocalDatabase.TryAssignGalleryUserTagsToManyRows(rowsSnap, tagsSnap, out addTouched);
                        if (success) touched = Math.Max(touched, addTouched);
                    }
                    touchedOut[0] = touched;
                    ok[0] = success ? 1 : 0;
                }
                catch { ok[0] = 0; touchedOut[0] = 0; }
                finally { System.Threading.Interlocked.Exchange(ref done[0], 1); }
            });

            while (System.Threading.Interlocked.CompareExchange(ref done[0], 0, 0) == 0)
                yield return null;

            if (ok[0] == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.db_fail", "Update failed (database)."), 2.2f);
                yield break;
            }

            // Refresh: treat as assign for pin/filter sync; also pass removed names when replace.
            if (removeSnap != null && removeSnap.Count > 0)
                RefreshUiAfterUserTagMutate(remove: true, rowsSnap, removeSnap);
            RefreshUiAfterUserTagMutate(remove: false, rowsSnap, tagsSnap);

            int itemN = NormalizeUserTagMutationTargets(selectedFiles).Count;
            if (itemN <= 0) itemN = targets.Count;
            if (replace)
            {
                ShowTemporaryStatus(string.Format(
                    VPBTranslation.T(
                        "gallery.detail.tags_replaced_fmt",
                        "Replaced tags on {0} items ({1} tags)"),
                    itemN, tags.Count), 2.2f);
            }
            else
            {
                ShowTemporaryStatus(string.Format(
                    VPBTranslation.T(
                        "gallery.detail.tags_pasted_fmt",
                        "Added {0} tags to {1} items"),
                    tags.Count, itemN), 2f);
            }
        }
    }
}
