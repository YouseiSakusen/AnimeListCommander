#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import os
import sys
import gi
import unicodedata

gi.require_version("Gimp", "3.0")
gi.require_version("Gio", "2.0")
gi.require_version("Gtk", "3.0")
from gi.repository import Gimp, Gio, GLib, GObject, Gtk

# --- 設定ファイルのパス (GitHub公開用にダミーパスを設定。適宜書き換えてください) ---
# ヒント: 環境変数等から取得するように拡張するとより汎用的になります。
DEFAULT_MACRO_PATH = r"C:\path\to\your\macro-settings.txt"
MACRO_SETTINGS_PATH = os.environ.get("GIMP_ANIME_MACRO_SETTINGS", DEFAULT_MACRO_PATH)
WORK_SETTINGS_FILENAME = "work-settings.txt"

def show_message_dialog(message, title="マクロ通知", is_error=False):
    """メッセージを画面中央にポップアップさせ、同時にコンソールにも出力する"""
    print(f"[{title}] {message}")
    Gimp.message(message) 
    dialog = Gtk.MessageDialog(
        parent=None,
        flags=Gtk.DialogFlags.MODAL,
        message_type=Gtk.MessageType.ERROR if is_error else Gtk.MessageType.INFO,
        buttons=Gtk.ButtonsType.OK,
        text=title
    )
    dialog.format_secondary_text(message)
    dialog.set_position(Gtk.WindowPosition.CENTER)
    dialog.set_keep_above(True)
    dialog.run()
    dialog.destroy()

def read_settings(path):
    data = {}
    if not os.path.exists(path): return data
    try:
        with open(path, encoding="utf-8") as f:
            section = None
            for line in f:
                line = line.rstrip()
                if not line: continue
                if line.startswith("#"):
                    section = line[1:].strip()
                    data[section] = []
                elif section: data[section].append(line)
    except: pass
    return data

def find_layer_by_name(image, name):
    def recursive_search(layers):
        if not layers: return None
        for layer in layers:
            if layer.get_name().strip() == name: return layer
            if hasattr(layer, "get_children"):
                found = recursive_search(layer.get_children())
                if found: return found
        return None
    return recursive_search(image.get_layers())

def sanitize_filename(name):
    table = str.maketrans({'\\': '￥', '/': '／', ':': '：', '*': '＊', '?': '？', '"': '”', '<': '＜', '>': '＞', '|': '｜'})
    return name.translate(table)

def get_unique_png_path(path):
    if not os.path.exists(path): return path
    base, ext = os.path.splitext(path)
    counter = 1
    while True:
        new_path = f"{base}-{counter:02d}{ext}"
        if not os.path.exists(new_path): return new_path
        counter += 1

def get_visual_weight(text):
    weight = 0.0
    for char in text:
        if unicodedata.east_asian_width(char) in 'FWA':
            weight += 1.0
        else:
            weight += 0.5
    return weight

def offset_layer_y(layer, offset_y):
    _, x, y = layer.get_offsets()
    layer.set_offsets(x, y + offset_y)

def ask_to_save(image, display):
    Gimp.displays_flush()
    while Gtk.events_pending(): Gtk.main_iteration()
    try:
        proc = Gimp.get_pdb().lookup_procedure('gimp-display-set-zoom')
        if proc:
            cfg = proc.create_config()
            cfg.set_property('display', display)
            cfg.set_property('zoom-type', 0)
            cfg.set_property('scale', 3.8)
            proc.run(cfg)
    except: pass
    while Gtk.events_pending(): Gtk.main_iteration()
    dialog = Gtk.MessageDialog(parent=None, flags=Gtk.DialogFlags.MODAL, message_type=Gtk.MessageType.QUESTION,
                               buttons=Gtk.ButtonsType.YES_NO, text="反映が完了しました。")
    dialog.format_secondary_text("pngを書き出し、xcfを保存しますか？")
    dialog.set_position(Gtk.WindowPosition.CENTER)
    dialog.set_keep_above(True)
    response = dialog.run()
    dialog.destroy()
    return response == Gtk.ResponseType.YES

def get_flattened_drawable(file_path):
    if not file_path or not os.path.exists(file_path): return None
    try:
        img = Gimp.file_load(Gimp.RunMode.NONINTERACTIVE, Gio.File.new_for_path(file_path))
        return img, img.flatten()
    except: return None

class CreateSingleAnimeImage(Gimp.PlugIn):
    def do_query_procedures(self):
        return ["plug-in-create-single-anime-image"]

    def do_create_procedure(self, name):
        procedure = Gimp.ImageProcedure.new(self, name, Gimp.PDBProcType.PLUGIN, self.run, None)
        procedure.set_image_types("*")
        procedure.set_menu_label("1作品ごとの一覧画像生成")
        procedure.add_menu_path("<Image>/Filters/アニメ画像一覧")
        return procedure

    def run(self, procedure, run_mode, kv_image, drawables, config, run_data):
        msg_proc = Gimp.get_pdb().lookup_procedure('gimp-message-set-handler')
        if msg_proc:
            msg_cfg = msg_proc.create_config()
            msg_cfg.set_property('handler', 2)
            msg_proc.run(msg_cfg)

        kv_file = kv_image.get_file()
        if kv_file is None:
            show_message_dialog("現在の画像が一度も保存されていません。\n名前を付けて保存してから実行してください。", "中断", True)
            return procedure.new_return_values(Gimp.PDBStatusType.CANCEL, GLib.Error())
        
        kv_path = kv_file.get_path()
        kv_dir = os.path.dirname(kv_path)
        
        work_path = os.path.join(kv_dir, WORK_SETTINGS_FILENAME)
        if not os.path.exists(work_path):
            show_message_dialog(f"設定ファイルが見つかりません:\n{work_path}", "エラー", True)
            return procedure.new_return_values(Gimp.PDBStatusType.CANCEL, GLib.Error())
            
        if not os.path.exists(MACRO_SETTINGS_PATH):
            show_message_dialog(f"共通マクロ設定が見つかりません:\n{MACRO_SETTINGS_PATH}", "エラー", True)
            return procedure.new_return_values(Gimp.PDBStatusType.CANCEL, GLib.Error())

        work = read_settings(work_path)
        macro = read_settings(MACRO_SETTINGS_PATH)

        export_filenames = work.get("EXPORT_FILENAME", [])
        if not export_filenames or not export_filenames[0].strip():
            show_message_dialog("出力ファイル名（EXPORT_FILENAME）が空です。\nwork-settings.txt を確認してください。", "エラー", True)
            return procedure.new_return_values(Gimp.PDBStatusType.CANCEL, GLib.Error())

        tpl_list = macro.get("TEMPLATE_XCF_PATH", [""])
        template_path = tpl_list[0] if tpl_list else ""
        if not template_path or not os.path.exists(template_path):
            show_message_dialog(f"テンプレートが見つかりません:\n{template_path}", "エラー", True)
            return procedure.new_return_values(Gimp.PDBStatusType.CANCEL, GLib.Error())

        template_image = Gimp.file_load(Gimp.RunMode.NONINTERACTIVE, Gio.File.new_for_path(template_path))
        display = Gimp.Display.new(template_image)

        sync_targets = [("KEY_VISUAL", kv_path), ("PRODUCTION_LOGO", "COMPANY_LOGO_PATH", "UNRELEASED_COMPANY_LOGO"), ("BROADCAST_LOGO", "BROADCAST_LOGO_PATH", "UNRELEASED_BROADCAST_LOGO")]
        for target in sync_targets:
            l_key, path_key = target[0], target[1]
            src_path = kv_path if l_key == "KEY_VISUAL" else ""
            if l_key != "KEY_VISUAL":
                dir_path = macro.get(path_key, [""])[0]
                fn_list = work.get(l_key, [])
                if dir_path and fn_list:
                    fn = fn_list[0]
                    src_path = os.path.join(dir_path, fn if fn.lower().endswith(".xcf") else fn + ".xcf")
                if not src_path or not os.path.exists(src_path):
                    src_path = macro.get(target[2], [""])[0]
            
            t_layer = find_layer_by_name(template_image, l_key)
            if t_layer and src_path and os.path.exists(src_path):
                res = get_flattened_drawable(src_path)
                if res:
                    tmp_img, src_drawable = res
                    _, ox, oy = t_layer.get_offsets()
                    new_l = Gimp.Layer.new_from_drawable(src_drawable, template_image)
                    new_l.set_offsets(ox, oy)
                    template_image.insert_layer(new_l, t_layer.get_parent(), template_image.get_item_position(t_layer))
                    template_image.remove_layer(t_layer)
                    new_l.set_name(l_key)
                    tmp_img.delete()

        text_map = {"TITLE": 20, "TITLE_RUBY": 8, "COMPANY": 9, "CAST": 10, "STAFF": 10, "ORIGINAL": 13, "THEME_SONG": 10, "FIRST_BROADCAST": 10, "BROADCAST_TEXT": 10}
        has_ruby = len(work.get("TITLE_RUBY", [])) > 0
        for key, base_size in text_map.items():
            lines = work.get(key, [])
            t_layer = find_layer_by_name(template_image, key)
            if not t_layer: continue

            if not lines and key in ["CAST", "STAFF"]:
                lines = ["キャスト未発表" if key == "CAST" else "スタッフ未発表"]

            if lines:
                t_layer.set_visible(True)
                if key == "STAFF":
                    if lines[0] == "スタッフ未発表":
                        t_layer.set_text(lines[0])
                        fs = 10
                    else:
                        # 新キーワード判定ルール
                        keywords = ["監督", "シリーズ構成", "デザイン", "キャラクター", "色彩"]
                        markup_lines = []
                        for l in lines:
                            # いずれかのキーワードが含まれるかチェック
                            size = 10 if any(kw in l for kw in keywords) else 13
                            safe_text = l.replace("&","&amp;").replace("<","&lt;")
                            markup_lines.append(f'<span size="{int(size * 1024)}">{safe_text}</span>')
                        
                        markup = "\n".join(markup_lines)
                        try: t_layer.set_markup(markup)
                        except: t_layer.set_text("\n".join(lines))
                        fs = 10 
                else:
                    txt = "\n".join(lines[:16] if key=="CAST" else lines)
                    t_layer.set_text(txt)
                    fs = base_size
                    if key=="TITLE":
                        if has_ruby: 
                            fs=15; offset_layer_y(t_layer, 7)
                        else:
                            w = get_visual_weight(txt)
                            fs = next((s[1] for s in [(17.0,20),(17.5,19),(18.0,18),(18.5,17),(19.0,16),(21.0,15),(23.0,14),(25.0,13),(27.0,12),(29.0,11)] if w<=s[0]),10)
                            if fs == 13: offset_layer_y(t_layer, 5)
                            elif fs == 14: offset_layer_y(t_layer, 4)
                    elif key=="THEME_SONG" and len(lines)==1: offset_layer_y(t_layer,5)
                    elif key=="CAST":
                        is_fairouz = any("ﾌｧｲﾙｰｽﾞあい" in l for l in lines)
                        num_cast = len(lines)
                        if is_fairouz: fs = 12
                        elif lines[0] == "キャスト未発表": fs = 10
                        elif num_cast == 15: fs = 11
                        elif num_cast == 14: fs = 12
                        elif num_cast >= 16: fs = 10
                        else: fs = 13
                    elif key=="ORIGINAL": fs=10 if len(lines)>=4 else 13
                    elif key=="FIRST_BROADCAST": fs=9 if len("".join(lines)) >= 15 else 10
                    t_layer.set_font_size(float(fs), Gimp.Unit.pixel())
            else: 
                t_layer.set_visible(False)

        if not ask_to_save(template_image, display):
            return procedure.new_return_values(Gimp.PDBStatusType.SUCCESS, GLib.Error())

        try:
            p_list = macro.get("OUTPUT_IMAGE_PATH", [""])
            png_dir = p_list[0] if p_list else ""
            if not png_dir: raise ValueError("OUTPUT_IMAGE_PATH未設定")
            os.makedirs(png_dir, exist_ok=True)

            xcf_path = os.path.join(kv_dir, sanitize_filename(work.get("TITLE", ["untitled"])[0]) + ".xcf")
            png_path = get_unique_png_path(os.path.join(png_dir, work.get("EXPORT_FILENAME", ["output"])[0] + ".png"))

            template_image.set_file(Gio.File.new_for_path(xcf_path))
            save_proc = Gimp.get_pdb().lookup_procedure('gimp-xcf-save')
            save_cfg = save_proc.create_config()
            save_cfg.set_property('image', template_image)
            save_cfg.set_property('file', Gio.File.new_for_path(xcf_path))
            save_proc.run(save_cfg)
            
            temp_copy = template_image.duplicate()
            temp_drawable = temp_copy.flatten()
            
            export_proc = Gimp.get_pdb().lookup_procedure('gimp-file-save')
            if export_proc:
                exp_cfg = export_proc.create_config()
                exp_cfg.set_property('run-mode', Gimp.RunMode.NONINTERACTIVE)
                exp_cfg.set_property('image', temp_copy)
                exp_cfg.set_property('file', Gio.File.new_for_path(png_path))
                export_proc.run(exp_cfg)
            
            temp_copy.delete()
            template_image.clean_all()
            Gimp.displays_flush()
        except Exception as e:
            show_message_dialog(f"保存失敗: {str(e)}", "エラー", True)

        return procedure.new_return_values(Gimp.PDBStatusType.SUCCESS, GLib.Error())

Gimp.main(CreateSingleAnimeImage.__gtype__, sys.argv)