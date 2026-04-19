#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
AnimeListCommander - GIMP Plugin for Auto-arranging Anime Images
Developed by Gemini for YouseiSakusen
Target GIMP Version: 3.2.2 or later
"""

import os
import sys
import re
import gi

gi.require_version("Gimp", "3.0")
gi.require_version("Gio", "2.0")
gi.require_version("Gtk", "3.0")
from gi.repository import Gimp, Gio, GLib, GObject, Gtk

# --- 設定ファイルのパス (GitHub公開用にダミーパスを設定。適宜書き換えてください) ---
# ヒント: 環境変数等から取得するように拡張するとより汎用的になります。
DEFAULT_MACRO_PATH = r"C:\path\to\your\macro-settings.txt"
MACRO_SETTINGS_PATH = os.environ.get("GIMP_ANIME_MACRO_SETTINGS", DEFAULT_MACRO_PATH)
WORK_SETTINGS_FILENAME = "work-settings.txt"

def read_settings(path):
    data = {}
    if not os.path.exists(path): return data
    try:
        with open(path, encoding="utf-8") as f:
            section = None
            for line in f:
                line_stripped = line.strip()
                if not line_stripped: continue
                if line_stripped.startswith("#"):
                    section = line_stripped[1:].strip()
                    data[section] = []
                elif section is not None:
                    data[section].append(line_stripped)
    except: pass
    return data

def parse_broadcast_date(date_str):
    if not date_str or "未発表" in date_str:
        return (99, 99, 99, 99)
    match_date = re.search(r'(\d+)/(\d+)', date_str)
    match_time = re.search(r'(\d+):(\d+)', date_str)
    if not match_date: return (99, 99, 99, 99)
    try:
        month = int(match_date.group(1))
        day = int(match_date.group(2))
        hour = int(match_time.group(1)) if match_time else 0
        minute = int(match_time.group(2)) if match_time else 0
        return (month, day, hour, minute)
    except: return (99, 99, 99, 99)

def ask_resize_question(message):
    dialog = Gtk.MessageDialog(
        parent=None,
        flags=Gtk.DialogFlags.MODAL,
        message_type=Gtk.MessageType.QUESTION,
        buttons=Gtk.ButtonsType.YES_NO,
        text="キャンバスのリサイズ確認"
    )
    dialog.format_secondary_text(message)
    dialog.set_position(Gtk.WindowPosition.CENTER)
    dialog.set_keep_above(True)
    response = dialog.run()
    dialog.destroy()
    return response == Gtk.ResponseType.YES

def find_layer_by_name(image, name):
    for layer in image.get_layers():
        if layer.get_name().strip() == name:
            return layer
    return None

class CreateAnimeListImage(Gimp.PlugIn):
    def do_query_procedures(self):
        return ["plug-in-create-anime-list-image"]

    def do_create_procedure(self, name):
        procedure = Gimp.ImageProcedure.new(self, name, Gimp.PDBProcType.PLUGIN, self.run, None)
        procedure.set_image_types("*")
        procedure.set_menu_label("アニメ画像一覧を自動配置")
        procedure.add_menu_path("<Image>/Filters/アニメ画像一覧")
        return procedure

    def run(self, procedure, run_mode, image, drawables, config, run_data):
        # 1. メッセージ設定 (3.2.2 対応)
        msg_proc = Gimp.get_pdb().lookup_procedure('gimp-message-set-handler')
        if msg_proc:
            msg_cfg = msg_proc.create_config()
            msg_cfg.set_property('handler', 2) # 2: ERROR_CONSOLE
            msg_proc.run(msg_cfg)
        
        Gimp.displays_flush()
        macro = read_settings(MACRO_SETTINGS_PATH)
        
        if not macro:
            Gimp.message(f"設定ファイルが読み込めません:\n{MACRO_SETTINGS_PATH}\nパスが正しいか確認してください。")
            return procedure.new_return_values(Gimp.PDBStatusType.EXECUTION_ERROR, GLib.Error())

        try:
            root_path = macro["TARGET_ROOT_PATH"][0]
            sort_mode = macro["SORT_MODE"][0]
            header_path = macro["HEADER_XCF_PATH"][0]
        except (KeyError, IndexError):
            Gimp.message("設定エラー: macro-settings.txt の項目を確認してください。")
            return procedure.new_return_values(Gimp.PDBStatusType.EXECUTION_ERROR, GLib.Error())

        anime_list = []
        if not os.path.exists(root_path):
            Gimp.message(f"エラー: 画像ルートフォルダが見つかりません\n{root_path}")
            return procedure.new_return_values(Gimp.PDBStatusType.EXECUTION_ERROR, GLib.Error())

        folders = sorted(os.listdir(root_path))
        
        for folder_name in folders:
            if folder_name.lower() == "header": continue
            folder_path = os.path.join(root_path, folder_name)
            if not os.path.isdir(folder_path): continue
            
            work_path = os.path.join(folder_path, WORK_SETTINGS_FILENAME)
            if not os.path.exists(work_path): continue

            xcf_path = os.path.join(folder_path, folder_name + ".xcf")
            if not os.path.exists(xcf_path): continue

            work_data = read_settings(work_path)
            title_kana_list = work_data.get("META_TITLE_KANA", [])
            if not title_kana_list: continue

            broadcast_kana_list = work_data.get("META_BROADCAST_KANA", [])
            has_bc_kana = len(broadcast_kana_list) > 0 and broadcast_kana_list[0].strip()

            try:
                title_kana = title_kana_list[0]
                broadcast_kana = broadcast_kana_list[0] if has_bc_kana else ""
                first_broadcast = work_data["FIRST_BROADCAST"][0] if work_data.get("FIRST_BROADCAST") else ""
                anime_list.append({
                    "title": folder_name, "xcf": xcf_path, "title_kana": title_kana,
                    "broadcast_kana": broadcast_kana, "date_val": parse_broadcast_date(first_broadcast)
                })
            except: continue

        if "放送局" in sort_mode:
            def sort_broadcast(x):
                kana = x["broadcast_kana"]
                if not kana: return (2, "")
                if kana.startswith("@") or kana.startswith("＠"): return (1, kana[1:])
                return (0, kana)
            anime_list.sort(key=lambda x: (sort_broadcast(x), x["title_kana"]))
        else:
            anime_list.sort(key=lambda x: (x["date_val"], x["title_kana"]))

        # 配置処理
        main_image = image
        full_items = [{"title": "角見出し", "xcf": header_path}] + anime_list
        item_w, item_h, margin = 330, 300, 2
        
        merge_proc = Gimp.get_pdb().lookup_procedure('gimp-image-merge-visible-layers')
        
        for i, item in enumerate(full_items):
            row, col = i // 3, i % 3
            pos_x = margin + (col * (item_w + margin))
            pos_y = margin + (row * (item_h + margin))
            if os.path.exists(item["xcf"]):
                try:
                    temp_img = Gimp.file_load(Gimp.RunMode.NONINTERACTIVE, Gio.File.new_for_path(item["xcf"]))
                    merged_layer = None
                    if merge_proc:
                        merge_cfg = merge_proc.create_config()
                        merge_cfg.set_property('image', temp_img)
                        merge_cfg.set_property('merge-type', Gimp.MergeType.CLIP_TO_IMAGE)
                        result = merge_proc.run(merge_cfg)
                        if result.length() > 0:
                            merged_layer = result.index(1)
                    
                    if merged_layer:
                        new_layer = Gimp.Layer.new_from_drawable(merged_layer, main_image)
                        main_image.insert_layer(new_layer, None, 0)
                        new_layer.set_name(item["title"])
                        new_layer.set_offsets(pos_x, pos_y)
                    
                    temp_img.delete()
                except: pass

        total_count = len(full_items)
        Gimp.message(f"【完了】一覧配置終了: {total_count}件")
        Gimp.displays_flush()

        # リサイズ処理
        total_rows = (total_count + 2) // 3
        required_height = margin + (total_rows * (item_h + margin))
        current_height = main_image.get_height()

        do_resize = False
        if required_height > current_height:
            do_resize = True
        elif required_height < current_height:
            do_resize = ask_resize_question(f"キャンバスを {required_height}px に切り詰めますか？")

        if do_resize:
            main_image.resize(main_image.get_width(), required_height, 0, 0)
            bg_layer = find_layer_by_name(main_image, "背景")
            if bg_layer:
                bg_layer.set_lock_position(False)
                bg_layer.resize_to_image_size()
                fill_proc = Gimp.get_pdb().lookup_procedure('gimp-drawable-edit-fill')
                if fill_proc:
                    fill_cfg = fill_proc.create_config()
                    fill_cfg.set_property('drawable', bg_layer)
                    fill_cfg.set_property('fill-type', Gimp.FillType.BACKGROUND_FILL)
                    fill_proc.run(fill_cfg)

        Gimp.displays_flush()
        return procedure.new_return_values(Gimp.PDBStatusType.SUCCESS, GLib.Error())

Gimp.main(CreateAnimeListImage.__gtype__, sys.argv)