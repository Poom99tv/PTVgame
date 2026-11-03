"""
PTV – Game To Earn (TikTok)  +  Gift Jar Overlay (รวมในไฟล์เดียว)
Features:
  • Runs as a PTVgame Launcher tool (no separate license – access is handled by the Launcher)
  • Theme / language follow the Launcher settings (%LOCALAPPDATA%/PTVgame/settings.json)
  • EN / TH language switch (full UI)
  • Arabic numerals throughout
  • Preset manager (add / delete / duplicate / import / export JSON)
  • Gift map, Follow-once-per-user, Like target
  • TTS (edge-tts / pyttsx3)
  • Top-5 Likers / Gifters green-screen overlay
  • Gift dropdown with price (💎), image preview from gifts/ folder
  • 🆕 Gift Jar Overlay — ของขวัญที่ส่งมาในไลฟ์จะตกลงแก้วโหลพร้อม Physics
"""

import sys
import threading
import time
import queue
import asyncio
import os
import tempfile
import re
import urllib.request
import json
import copy
import math
import random
import glob
from datetime import datetime

import pydirectinput

from PyQt5.QtWidgets import (
    QApplication, QMainWindow, QVBoxLayout, QHBoxLayout,
    QWidget, QTextEdit, QLabel, QLineEdit, QPushButton, QFrame, QComboBox,
    QDialog, QSizePolicy, QScrollArea, QSpinBox, QTabWidget,
    QGridLayout, QFileDialog, QMessageBox, QCheckBox,
    QListWidget, QListWidgetItem, QProgressBar, QSlider, QSizeGrip
)
from PyQt5.QtCore import (
    pyqtSignal, QObject, Qt, QTimer, QRectF, QThread, QPointF, QLocale
)
from PyQt5.QtGui import (
    QPixmap, QPainter, QPainterPath, QPen, QColor, QIcon, QFont,
    QBrush, QRadialGradient, QLinearGradient
)

from TikTokLive import TikTokLiveClient
from TikTokLive.events import (
    ConnectEvent, DisconnectEvent, LiveEndEvent,
    GiftEvent, FollowEvent, LikeEvent, CommentEvent
)

# ══════════════════════════════════════════════════════════════════
#  PTVgame LAUNCHER INTEGRATION — paths, theme, language
# ══════════════════════════════════════════════════════════════════
FROZEN   = getattr(sys, "frozen", False)
APP_DIR  = os.path.dirname(os.path.abspath(sys.executable if FROZEN else __file__))
RES_DIR  = getattr(sys, "_MEIPASS", APP_DIR)
LAUNCHER_DIR = os.path.join(os.environ.get("LOCALAPPDATA") or os.path.expanduser("~"), "PTVgame")
DATA_DIR     = os.path.join(LAUNCHER_DIR, "GameToEarn")
PRESETS_FILE = os.path.join(DATA_DIR, "presets.json")
STATE_FILE   = os.path.join(DATA_DIR, "state.json")


def _read_json(path, fallback):
    try:
        with open(path, "r", encoding="utf-8-sig") as f:
            return json.load(f)
    except Exception:
        return fallback


def _write_json(path, data):
    try:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        tmp = path + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        os.replace(tmp, path)
    except Exception:
        pass


def _arg(name, default=None):
    if name in sys.argv:
        i = sys.argv.index(name)
        if i + 1 < len(sys.argv):
            return sys.argv[i + 1]
    return default


_LAUNCHER_SETTINGS = _read_json(os.path.join(LAUNCHER_DIR, "settings.json"), {}) or {}
THEME_NAME = _arg("--theme", _LAUNCHER_SETTINGS.get("theme") or "Midnight")
if THEME_NAME not in ("Midnight", "Ember", "Neon", "Frost"):
    THEME_NAME = "Midnight"
LAUNCHER_LANG = "TH" if str(_arg("--lang", _LAUNCHER_SETTINGS.get("language") or "en")).lower().startswith("th") else "EN"


def _mix(a, b, t):
    a = a.lstrip("#"); b = b.lstrip("#")
    ca = [int(a[i:i + 2], 16) for i in (0, 2, 4)]
    cb = [int(b[i:i + 2], 16) for i in (0, 2, 4)]
    return "#" + "".join("%02X" % round(x + (y - x) * t) for x, y in zip(ca, cb))


# same colours as LauncherShell.Build()
_ACCENTS = {"Midnight": "#268EFF", "Ember": "#FF7438", "Neon": "#BF48FF", "Frost": "#268EFF"}
if THEME_NAME == "Frost":
    P = {
        "bg0": "#F4F8FF", "bg": "#ECF5FF", "panel": "#FFFFFF", "panel2": "#E6EFF9",
        "line": "#C9DCEF", "line2": "#B3CBE3", "dim": "#7D95AC", "muted": "#57748E",
        "soft": "#34506B", "text": "#102B48", "frame": "#27435C",
        "grad_a": "#DDF1FF", "grad_b": "#F7FAFF",
    }
else:
    P = {
        "bg0": "#00101E", "bg": "#00101F", "panel": "#06243D", "panel2": "#0A2D4B",
        "line": "#1D4366", "line2": "#27435C", "dim": "#5F82A3", "muted": "#88AAC9",
        "soft": "#B6D1EE", "text": "#EAF3FF", "frame": "#27435C",
        "grad_a": "#03223A", "grad_b": "#00111F",
    }
P["accent"]    = _ACCENTS[THEME_NAME]
P["accent_bg"] = _mix(P["panel"], P["accent"], 0.22)
P["accent_hi"] = _mix(P["accent"], "#FFFFFF", 0.18)
P["console"]   = "#00101E" if THEME_NAME != "Frost" else "#0B2238"
P["header_line"] = "#183550" if THEME_NAME != "Frost" else "#C9DCEF"

# old TikTok-dark palette -> launcher palette
_COLOR_MAP = {}
for _role, _olds in {
    "bg0":    ("#060606", "#080808", "#0a0a0a"),
    "bg":     ("#0c0c0c", "#0d0d0d", "#0f0f0f"),
    "panel":  ("#111", "#111111", "#141414", "#161616"),
    "panel2": ("#1a1a1a", "#181818", "#1e1e1e"),
    "line":   ("#222", "#222222", "#242424", "#2a2a2a"),
    "line2":  ("#333", "#333333", "#3a3a3a"),
    "dim":    ("#444", "#444444", "#555", "#555555"),
    "muted":  ("#666", "#666666", "#888", "#888888", "#999", "#999999", "#aaa", "#aaaaaa"),
    "soft":   ("#b0b0b0", "#bbb", "#bbbbbb", "#c0c0c0", "#ccc", "#cccccc"),
    "text":   ("#e0e0e0", "#f0f0f0"),
    "accent": ("#fe2c55",),
    "accent_bg": ("#1a0008", "#1a0010"),
}.items():
    for _o in _olds:
        _COLOR_MAP[_o] = P[_role]

_HEX_RE   = re.compile(r"#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b")
_WHITE_RE = re.compile(r"(?<![-\w])color\s*:\s*(white|#fff\b|#ffffff\b)", re.IGNORECASE)
_BG_RE    = re.compile(r"background(?:-color)?\s*:\s*(#[0-9a-fA-F]{3,6})", re.IGNORECASE)


def qss(css):
    """Recolour a style sheet written for the old dark TikTok theme to the Launcher theme."""
    if not css:
        return css
    css = str(css)
    if THEME_NAME == "Frost":
        # white text only stays white on coloured (non-grey) buttons
        bgs = [b.lower() for b in _BG_RE.findall(css)]
        coloured = any(b not in _COLOR_MAP or b == "#fe2c55" for b in bgs)
        if not coloured:
            css = _WHITE_RE.sub("color:" + P["text"], css)
    return _HEX_RE.sub(lambda m: _COLOR_MAP.get(m.group(0).lower(), m.group(0)), css)


def _ss(widget, css):
    widget.setStyleSheet(qss(css))


def load_presets():
    data = _read_json(PRESETS_FILE, None)
    if isinstance(data, list) and data and all(isinstance(p, dict) for p in data):
        return data
    return copy.deepcopy(DEFAULT_PRESETS)


def save_presets(presets):
    _write_json(PRESETS_FILE, presets)


HAS_TTS = True
try:
    import pyttsx3
except Exception:
    HAS_TTS = False

HAS_EDGE_TTS = True
try:
    import edge_tts
    import pygame
except Exception:
    HAS_EDGE_TTS = False

# ══════════════════════════════════════════════════════════════════
#  CONFIGURATION
# ══════════════════════════════════════════════════════════════════
GIFTS_FOLDER = os.path.join(RES_DIR, "gifts")

_URL_RE = re.compile(r"(https?://[^\s'\"<>]+)", re.IGNORECASE)

# ══════════════════════════════════════════════════════════════════
#  JAR PHYSICS CONSTANTS
# ══════════════════════════════════════════════════════════════════
JAR_W        = 220
JAR_H        = 280
JAR_WIN_W    = 500
JAR_WIN_H    = 580
GRAVITY      = 0.42
BOUNCE       = 0.01        # ลด bounce ให้กองชิดกันขึ้น
FRICTION     = 0.82        # เพิ่ม friction ให้นิ่งเร็ว
GIFT_SIZE    = 32          # เล็กลงเล็กน้อย ให้จุได้มากขึ้น
JAR_SETTINGS_FILE = os.path.join(DATA_DIR, "jar_settings.json")

# ══════════════════════════════════════════════════════════════════
#  GIFT CATALOG
# ══════════════════════════════════════════════════════════════════
GIFT_CATALOG = [
    # กลุ่ม 1-10 เหรียญ
    ("Rose", 1), ("TikTok", 1), ("GG", 1), ("Pop", 1), ("You're awesome", 1),
    ("Creeper", 1), ("Love you so much", 1), ("Oldies", 1), ("Freestyle", 1),
    ("Cake Slice", 1), ("Glow Stick", 1), ("Ice Cream Cone", 1), ("Wink wink", 1),
    ("Heart Me", 1), ("Congratulations", 1), ("So Cute", 1), ("Placeholder", 1),
    ("Light", 1), ("Thumbs Up", 1), ("Heart", 1), ("Love you", 1),
    ("Thai Popsicle", 1), ("Music Album", 1), ("Enjoy Music", 1), ("Wink Charm", 1),
    ("Go Popular", 1), ("Club Cheers", 1),
    ("Team Bracelet", 2),
    ("Finger Heart", 5), ("Overreact", 5), ("Name shoutout", 5), ("Duit Raya", 5),
    ("The Lucky 9", 9), ("Super Popular", 9), ("Cheer You Up", 9), ("Club Power", 9),
    ("Rosa", 10), ("BFF Necklace", 10), ("Journey Pass", 10), ("Slow motion", 10),
    ("Chocolate", 10), ("League Ball", 10), ("Lucky Pony", 10), ("I love you", 10),
    ("Style Me Up", 10), ("Heart Gaze", 10),
    ("Bravo!", 15), ("Flower Garland", 15), ("little hippo", 15), ("Live Up", 15),
    ("Perfume", 20),
    ("Doughnut", 30),
    ("Paper Crane", 99), ("Little Crown", 99), ("Cap", 99), ("Hat and Mustache", 99),
    ("Like-Pop", 99), ("Love Painting", 99), ("Bubble Gum", 99), ("Mark of Love", 99),
    ("Sundae Bowl", 99), ("Charmer Bow", 99), ("Club Victory", 99),
    ("Level-up Sparks", 99), ("Greeting Heart", 99),
    ("Game Controller", 100), ("Super GG", 100), ("Mishka Bear", 100),
    ("Confetti", 100), ("Hand Heart", 100), ("Singing Magic", 100),
    ("Marvelous Confetti", 100),
    ("Bowknot", 149), ("Big Shout Out", 149), ("Chatting Popcorn", 149),
    ("Masquerade", 149), ("Balloon Crown", 149), ("Feather Tiara", 149),
    ("Caterpillar Chaos", 149), ("Catrina", 149), ("Raving Snail", 149),
    ("Santa Cocoa", 149), ("Love Glasses", 149),
    ("Song of Harvest", 150),
    ("League Countdown", 199), ("Sunglasses", 199), ("Hearts", 199),
    ("Garland Headpiece", 199), ("Love You", 199), ("Cheer For You", 199),
    ("Stinging Bee", 199), ("Massage for You", 199), ("Coffee Magic", 199),
    ("Lots of Bread", 199), ("Cheering Crab", 199), ("Dancing Hands", 199),
    ("Night Star", 199), ("Twinkling Star", 199), ("Floating Octopus", 199),
    ("Flower Headband", 199), ("Goalkeeper Save", 199), ("Bowtiful Crown", 199),
    ("Sour Buddy", 199), ("Panda Climb", 199), ("Rose Hand", 199),
    ("Melon Juice", 199), ("Coconut Juice", 199), ("Chirpy Kisses", 199),
    ("Joker Ball", 199), ("Juicy Smile", 199), ("Side by Side", 199),
    ("Heart Hood", 199), ("Party Pony", 199),
    ("Balloons", 200), ("Magic Genie", 200),
    ("Rose Bear", 214),
    ("Cat", 222),
    ("Pinch Face", 249), ("Candy Bouquet", 249), ("Ice Cream Mic", 249),
    ("Star Goggles", 249), ("Cheer Mic", 249), ("Music Bubbles", 249),
    ("Palm Breeze", 249), ("Forest Elf", 249), ("Face-pulling", 249),
    ("Treasured Voice", 249), ("Melodic birds", 249), ("Surfing Penguin", 249),
    ("Party Blossom", 249), ("Dreamy Strings", 249), ("Sweet Flutter", 249),
    ("Snow Bloom", 249),
    ("LIVE Ranking Crown", 299), ("EID Gift Box", 299), ("Corgi", 299),
    ("Fruit Friends", 299), ("Naughty Chicken", 299), ("Play for You", 299),
    ("Rock Star", 299), ("Butterfly for You", 299), ("Puppy Kisses", 299),
    ("Spider on web", 299), ("United Heart", 299), ("Kicker Challenge", 299),
    ("Hawaiian Lei", 299), ("Hi! Rosie!", 299), ("Pawfect", 299),
    ("Go Hamster", 299), ("Bat Headwear", 299), ("Melody Glasses", 299),
    ("Penguin Snowpal", 299), ("Judy Pose", 299), ("Music Mate", 299),
    ("Love Call", 299), ("Pony Lantern", 299), ("Spring Sprout", 299),
    ("Budding Heart", 299),
    ("Feather Mask", 300),
    ("Backing Monkey", 349), ("Become Kitten", 349), ("Marked with Love", 349),
    ("Vinyl Flip", 349), ("Juicy Cap", 349), ("Batwing Hat", 349),
    ("Mystic Drink", 349), ("Beach Maracas", 349), ("Vintage flight", 349),
    ("Gingerbread Man", 349), ("Rocking Shroom", 349), ("Sparkle Pony", 349),
    ("Spring Bouquet", 349),
    ("Forever Rosa", 399), ("Magic Rhythm", 399), ("Relaxed Goose", 399),
    ("Tom's Hug", 399), ("Rosie the Rose Bean", 399), ("Jollie the Joy Bean", 399),
    ("Rocky the Rock Bean", 399), ("Sage the Smart Bean", 399), ("Sage's Slash", 399),
    ("Let butterfly dances", 399), ("Kitten Kneading", 399), ("Shoot the Apple", 399),
    ("Alien Buddy", 399), ("Rosie's Concert", 399), ("Tiger Lift", 399),
    ("Santa Owl Surprise", 399), ("Confetti Bear", 399), ("Singing Sax", 399),
    ("Fairy Locket", 399), ("Panda Snap", 399), ("Blossom Fairy", 399),
    ("Crystal Dreams", 400), ("Wishing Cake", 400), ("Mic Champ", 400),
    ("Bounce Speakers", 400), ("Reindeer Milk", 400), ("Judges voting", 400),
    ("Beating Heart", 449), ("Encore Clap", 449), ("Pirate's Treasure", 449),
    ("Candy Loot", 449), ("Captured Vocals", 449), ("Batting Cutie", 449),
    ("Space Love", 449), ("Xmas Tree Hat", 449),
    ("Powerful Mind", 450), ("Hat of Joy", 450), ("Halloween Fun Hat", 450),
    ("Music Conductor", 450), ("Celebration Hat", 450), ("City Pop", 450),
    ("Cupid Koala", 450), ("Paw Call", 450), ("Superwoman", 450),
    ("Crystal Heart", 499), ("Coral", 499), ("Rose Soundwave", 499),
    ("Basket of Spring", 499), ("Hands Up", 499),
    ("Money Gun", 500), ("You're Amazing", 500), ("VR Goggles", 500),
    ("DJ Glasses", 500), ("Dragon Crown", 500), ("Racing Helmet", 500),
    ("Goldfish Scooping", 500), ("Bunny Crown", 500), ("Magic Prop", 500),
    ("Cozy Xmas Set", 500), ("Mystery Box", 500), ("Heart Guitar", 500),
    ("Spark ring", 500), ("Baby Chicks", 500), ("Gem Gun", 500),
    ("Bubbly Kiss", 530),
    ("Fully Bloomed Sakura", 599), ("League Trophy", 599),
    ("Join Butterflies", 600),
    ("Swan", 699),
    ("Colorful Wings", 700),
    ("Love Flight", 800),
    ("Train", 899),
    ("Travel with You", 999), ("Lucky Airdrop Box", 999), ("Grand show", 999),
    ("Trending Figure", 999),
    ("Watermelon Love", 1000), ("Blooming Ribbons", 1000), ("Galaxy", 1000),
    ("Fairy Wings", 1000), ("Flamingo Groove", 1000), ("Lightning your world", 1000),
    ("Sparkle Dance", 1000), ("Shiny air balloon", 1000),
    ("Joy Floats", 1030), ("Candy Puffs", 1030),
    ("Fireworks", 1088), ("Magic Role", 1088),
    ("Exclusive Spark", 1099),
    ("Umbrella of Love", 1200),
    ("Frozen Magic", 1299),
    ("Autumn Picnic", 1300), ("Party Laser", 1300),
    ("Vibrant Stage", 1400),
    ("Level Ship", 1500), ("Raya Gift Card", 1500), ("Viking Hammer", 1500),
    ("Chasing the Dream", 1500), ("Lover's Lock", 1500), ("Greeting Card", 1500),
    ("Future Encounter", 1500), ("Under Control", 1500), ("Racing Debut", 1500),
    ("You're So Fly", 1500), ("Astrobear", 1500), ("Wild Mic", 1500),
    ("Shooting Stars", 1580),
    ("Blooming Heart", 1599),
    ("Here We Go", 1799),
    ("Love Drop", 1800), ("Fox Legend", 1800),
    ("Doll New Year Greeting", 1999), ("Star of Red Carpet", 1999),
    ("Cooper Flies Home", 1999), ("Mystery Firework", 1999),
    ("Cooper Picnic", 2000), ("Sky Drift", 2000),
    ("Whale Diving", 2150),
    ("Blow Rosie Kisses", 2199), ("Jollie's Heartland", 2199),
    ("Rocky's Punch", 2199), ("Sage's Coinbot", 2199),
    ("Stargazing", 2200), ("Wave Lights", 2200),
    ("By the Glaziers", 2380),
    ("Hit The Buzzer", 2488),
    ("Unicorn", 2499),
    ("Animal Band", 2500),
    ("Stroke Me", 2800),
    ("Samfaring Tom", 2850),
    ("Motorcycle", 2988), ("Pink Dream", 2988), ("Ice Cream Truck", 2988),
    ("Ring Of Honor-Cube", 2999), ("Rhythmic Bear", 2999), ("Love in Sunset", 2999),
    ("Party Bus", 2999), ("Ocelot Chase", 2999), ("Surprise Baby Mob", 2999),
    ("Level-up Spotlight", 2999),
    ("Meteor Shower", 3000), ("Balloon and Dart", 3000),
    ("Hip-Hop Hen", 3200),
    ("Go Home", 3999),
    ("Shine Bright", 4088), ("Magic World", 4088),
    ("Your Concert", 4500),
    ("Private Jet", 4888), ("Leon the Kitten", 4888), ("Fiery Dragon", 4888),
    ("Signature Jet", 4888),
    ("Sugar Whiskers", 4918),
    ("Sage's Venture", 4999), ("Hero Space Ship", 4999),
    ("League Fandom", 5000), ("Diamond Gun", 5000), ("Flying Jets", 5000),
    ("Chopin's Nocturne", 5000), ("Pink Star Gun", 5000), ("Spark stage", 5000),
    ("Fluffy Buddies", 5388),
    ("Wolf", 5500),
    ("Cub on Clouds", 5888), ("Valiant Odyssey", 5888),
    ("Devoted Heart", 5999),
    ("Future City", 6000), ("Sam in New City", 6000), ("Work Hard Play Harder", 6000),
    ("Strong Finish", 6000), ("Zombie Swarm", 6000), ("Chick Stampede", 6000),
    ("Lili the Leopard", 6599),
    ("TikTok Red Carpet", 6600),
    ("Celebration Time", 6999), ("Happy Party", 6999),
    ("Sports Car", 7000), ("Neon Ferris Wheel", 7000),
    ("Majestic Hearts", 7238),
    ("Star Throne", 7999),
    ("Leon and Lili", 9699),
    ("Interstellar", 10000), ("Sunset Speedway", 10000),
    ("Red Lightning", 12000),
    ("Level-up Spectacle", 12999),
    ("Invincible Hammer", 14999), ("Tidecaller Trident", 14999),
    ("Leopard", 15000), ("Golden Gallop", 15000), ("Paris", 15000),
    ("Rosa Nebula", 15000), ("Future Journey", 15000), ("Match star", 15000),
    ("Party On&On", 15000), ("DJ Peak", 15000), ("Sneaky Jockey", 15000),
    ("Mystery Stalker", 15000),
    ("Amusement Park", 17000),
    ("Fly Love", 19999),
    ("Castle Fantasy", 20000), ("TikTok Shuttle", 20000), ("Premium Shuttle", 20000),
    ("Level Ship", 21000),
    ("Infinite Heart", 23999),
    ("Rose Carriage", 25000),
    ("Cyber Roar", 25999), ("Phoenix", 25999), ("Adam's Dream", 25999),
    ("Undersea Kingdom", 25999),
    ("Dragon Flame", 26999),
    ("Lion", 29999),
    ("Zeus", 34000), ("Leon and Lion", 34000),
    ("TikTok Universe+", 34999),
    ("TikTok Stars", 39999), ("Thunder Falcon", 39999),
    ("Fire Phoenix", 41999),
    ("Legend Marcellus", 42999), ("Pegasus", 42999),
    ("Julius the Champion", 43999),
    ("TikTok Universe", 44999),
]

GIFT_PRICE_MAP = {name.lower(): price for name, price in GIFT_CATALOG}

# ──────────────────────────────────────────────────────────────────
#  รูปภาพของขวัญ
# ──────────────────────────────────────────────────────────────────
_gift_image_cache = {}


def _normalize_filename(name):
    return name.strip().lower()


def get_gift_pixmap(gift_name):
    key = _normalize_filename(gift_name)
    if key in _gift_image_cache:
        return _gift_image_cache[key]
    pix = None
    if os.path.isdir(GIFTS_FOLDER):
        extensions = [".png", ".jpg", ".jpeg", ".webp", ".gif"]
        for fname in os.listdir(GIFTS_FOLDER):
            name_no_ext, ext = os.path.splitext(fname)
            if name_no_ext.lower() == key and ext.lower() in extensions:
                full_path = os.path.join(GIFTS_FOLDER, fname)
                test = QPixmap(full_path)
                if not test.isNull():
                    pix = test
                break
    _gift_image_cache[key] = pix
    return pix


def format_price(price):
    return f"{price:,}"


# ══════════════════════════════════════════════════════════════════
#  JAR SETTINGS
# ══════════════════════════════════════════════════════════════════
def load_jar_settings():
    d = {"scale": 100}
    if os.path.exists(JAR_SETTINGS_FILE):
        try:
            with open(JAR_SETTINGS_FILE, "r") as f:
                d.update(json.load(f))
        except Exception:
            pass
    return d


def save_jar_settings(data):
    try:
        os.makedirs(DATA_DIR, exist_ok=True)
        with open(JAR_SETTINGS_FILE, "w") as f:
            json.dump(data, f)
    except Exception:
        pass


# ══════════════════════════════════════════════════════════════════
#  GIFT PARTICLE PHYSICS
# ══════════════════════════════════════════════════════════════════
class GiftParticle:
    def __init__(self, x, y, pix):
        self.x        = float(x)
        self.y        = float(y)
        self.vx       = random.uniform(-0.6, 0.6)
        self.vy       = random.uniform(2.0, 4.0)
        self.rot      = random.uniform(-35, 35)      # สุ่มมุมเริ่มต้น ±35°
        self.vrot     = random.uniform(-1.2, 1.2)    # หมุนระหว่างตก
        self._final_rot = random.uniform(-30, 30)    # มุมสุดท้ายที่จะนอนค้าง
        self.pix      = pix
        self.r        = GIFT_SIZE / 2.0 * 0.88       # radius เล็กกว่า visual เพื่อให้ชิดกัน
        self.overflow = False
        self.is_sleeping = False
        self.sleep_timer = 0
        self.mass     = 1.0

    def update_physics_and_bounds(self, floor_y, out_floor_y,
                                  jar_x1, jar_x2, jar_top, jar_cx):
        if self.is_sleeping:
            return
        self.vy  += GRAVITY
        self.x   += self.vx
        self.y   += self.vy
        self.rot += self.vrot
        self.vx  *= 0.88
        self.vrot *= 0.90

        current_floor = out_floor_y if self.overflow else floor_y

        if self.y + self.r >= current_floor:
            self.y  = current_floor - self.r
            self.vy = -self.vy * BOUNCE
            self.vx *= FRICTION
            if abs(self.vy) < 0.2: self.vy = 0.0
            if abs(self.vx) < 0.1: self.vx = 0.0

        if not self.overflow:
            current_x1 = jar_x1
            current_x2 = jar_x2
            if jar_top <= self.y <= (jar_top + 32):
                current_x1 = jar_x1 + 24
                current_x2 = jar_x2 - 24
            if self.y > jar_top:
                if self.x - self.r < current_x1:
                    self.x  = current_x1 + self.r
                    self.vx = -self.vx * BOUNCE
                elif self.x + self.r > current_x2:
                    self.x  = current_x2 - self.r
                    self.vx = -self.vx * BOUNCE
            if self.y <= jar_top + 5:
                neck_x1 = jar_x1 + 24
                neck_x2 = jar_x2 - 24
                if self.x < neck_x1:
                    self.overflow = True
                    self.vx = -abs(self.vx) - 0.5
                elif self.x > neck_x2:
                    self.overflow = True
                    self.vx = abs(self.vx) + 0.5

        if self.x - self.r < 0:
            self.x = self.r
            self.vx = abs(self.vx) * BOUNCE
        elif self.x + self.r > JAR_WIN_W:
            self.x = JAR_WIN_W - self.r
            self.vx = -abs(self.vx) * BOUNCE

    def resolve_collision(self, other, jar_top, jar_x1, jar_x2, jar_cx):
        if self.is_sleeping and other.is_sleeping:
            return
        dx   = self.x - other.x
        dy   = self.y - other.y
        dist = math.hypot(dx, dy)
        min_d = self.r + other.r - 0.2
        if 0 < dist < min_d:
            nx = dx / dist
            ny = dy / dist
            overlap = min_d - dist
            self_mass  = 2.0 if self.y > other.y else 1.0
            other_mass = 2.0 if other.y > self.y else 1.0
            if self.is_sleeping:  self_mass  = 10.0
            if other.is_sleeping: other_mass = 10.0
            total_mass = self_mass + other_mass
            wm1 = other_mass / total_mass
            wm2 = self_mass  / total_mass
            if not self.is_sleeping:
                self.x  += nx * overlap * wm1
                self.y  += ny * overlap * wm1
            if not other.is_sleeping:
                other.x -= nx * overlap * wm2
                other.y -= ny * overlap * wm2
            if self.is_sleeping and overlap > 1.8:
                self.is_sleeping = False; self.sleep_timer = 0
            if other.is_sleeping and overlap > 1.8:
                other.is_sleeping = False; other.sleep_timer = 0
            kx = self.vx - other.vx
            ky = self.vy - other.vy
            p_dist = kx * nx + ky * ny
            if p_dist < 0:
                if self.overflow or other.overflow:
                    current_bounce = 0.0
                else:
                    current_bounce = BOUNCE if (self.y < jar_top + 100) else 0.0
                impulse = (1.0 + current_bounce) * p_dist / total_mass
                if not self.is_sleeping:
                    self.vx -= impulse * other_mass * nx
                    self.vy -= impulse * other_mass * ny
                if not other.is_sleeping:
                    other.vx += impulse * self_mass * nx
                    other.vy += impulse * self_mass * ny
            if not self.overflow and self.y < jar_top:
                neck_x1 = jar_x1 + 24
                neck_x2 = jar_x2 - 24
                if self.x < (neck_x1 + 5) or self.x > (neck_x2 - 5):
                    self.overflow = True
                    self.is_sleeping = False

    def check_sleep_state(self):
        if abs(self.vx) < 0.12 and abs(self.vy) < 0.12 and abs(self.vrot) < 0.12:
            self.sleep_timer += 1
            if self.sleep_timer > 4:
                self.is_sleeping = True
                self.vx = 0.0; self.vy = 0.0; self.vrot = 0.0
                # ค่อยๆ หมุนไปหามุมสุดท้ายที่สุ่มไว้ (เพื่อไม่ให้ตรงตลอด)
                diff = self._final_rot - self.rot
                if abs(diff) > 0.5:
                    self.rot += diff * 0.15
                else:
                    self.rot = self._final_rot
        else:
            self.sleep_timer = 0

    def draw(self, p, scale=1.0):
        p.save()
        p.translate(self.x * scale, self.y * scale)
        p.rotate(self.rot)
        s = int(GIFT_SIZE * scale)
        p.drawPixmap(-s // 2, -s // 2, s, s, self.pix)
        p.restore()


# ══════════════════════════════════════════════════════════════════
#  JAR OVERLAY WINDOW
# ══════════════════════════════════════════════════════════════════
def _load_jar_gift_pixmaps():
    """โหลดรูปของขวัญทั้งหมดจากโฟลเดอร์ gifts/ สำหรับ jar"""
    pix_list = []
    name_map = {}   # gift_name_lower → QPixmap (scaled)
    if not os.path.isdir(GIFTS_FOLDER):
        return pix_list, name_map
    extensions = [".png", ".jpg", ".jpeg", ".webp", ".gif"]
    for fname in sorted(os.listdir(GIFTS_FOLDER)):
        name_no_ext, ext = os.path.splitext(fname)
        if ext.lower() not in extensions:
            continue
        path = os.path.join(GIFTS_FOLDER, fname)
        px = QPixmap(path)
        if px.isNull():
            continue
        px_scaled = px.scaled(GIFT_SIZE, GIFT_SIZE,
                               Qt.KeepAspectRatio, Qt.SmoothTransformation)
        pix_list.append(px_scaled)
        name_map[name_no_ext.lower()] = px_scaled
    return pix_list, name_map


class GiftJarOverlay(QWidget):
    """หน้าต่างแก้วโหลแบบ transparent overlay"""

    def __init__(self):
        super().__init__()
        self.setWindowFlags(
            Qt.FramelessWindowHint |
            Qt.WindowStaysOnTopHint
        )
        self.setAttribute(Qt.WA_TranslucentBackground, True)
        self.setStyleSheet("background: transparent;")

        self._scale   = 1.0
        self._win_w   = JAR_WIN_W
        self._win_h   = JAR_WIN_H
        self.resize(JAR_WIN_W, JAR_WIN_H)

        screen = QApplication.primaryScreen().geometry()
        self.move(screen.width() - JAR_WIN_W - 20,
                  screen.height() - JAR_WIN_H - 60)

        self._pix_list, self._pix_map = _load_jar_gift_pixmaps()
        self._particles = []

        self._jar_cx   = JAR_WIN_W // 2
        self._jar_top  = 210
        self._jar_bot  = self._jar_top + JAR_H
        self._jar_x1   = self._jar_cx - JAR_W // 2 + 18
        self._jar_x2   = self._jar_cx + JAR_W // 2 - 18
        self._floor_y      = self._jar_bot - 18
        self._out_floor_y  = self._jar_bot - 18

        self._tick_timer = QTimer(self)
        self._tick_timer.timeout.connect(self._tick)
        self._tick_timer.start(16)

        self._oldPos = None

    def reload_images(self):
        """โหลดรูปใหม่ (เรียกได้หลัง gifts/ เปลี่ยน)"""
        self._pix_list, self._pix_map = _load_jar_gift_pixmaps()

    # ── public API ────────────────────────────────────────────────
    def add_gift(self, gift_name=None):
        """เพิ่มของขวัญ 1 ชิ้นลงโหล  gift_name=None → สุ่ม"""
        pix = None
        if gift_name:
            pix = self._pix_map.get(gift_name.lower().strip())
        if pix is None and self._pix_list:
            pix = random.choice(self._pix_list)
        if pix is None:
            return

        neck_x1 = self._jar_x1 + 24
        neck_x2 = self._jar_x2 - 24
        spawn_x = random.uniform(neck_x1 + 10, neck_x2 - 10)
        spawn_y = -GIFT_SIZE

        part = GiftParticle(spawn_x, spawn_y, pix)
        self._particles.append(part)

        # ปลุกชิ้นส่วนบนสุดที่หลับอยู่ให้รับแรงกระแทก
        for pt in self._particles[:-1]:
            if pt.is_sleeping and pt.y < self._jar_top + 100:
                pt.is_sleeping = False
                pt.sleep_timer = 0

    def drop_gifts(self, gift_name, count):
        """หล่นของขวัญหลายชิ้นทีละ 80ms"""
        count = min(int(count), 15)
        for i in range(count):
            QTimer.singleShot(i * 80, lambda gn=gift_name: self.add_gift(gn))

    def set_scale(self, pct):
        self._scale = pct / 100.0
        self._win_w = int(JAR_WIN_W * self._scale)
        self._win_h = int(JAR_WIN_H * self._scale)
        self.resize(self._win_w, self._win_h)

    def clear_particles(self):
        self._particles.clear()

    def particle_count(self):
        return len(self._particles)

    # ── physics tick ─────────────────────────────────────────────
    def _tick(self):
        for pt in self._particles:
            pt.update_physics_and_bounds(
                self._floor_y, self._out_floor_y,
                self._jar_x1, self._jar_x2,
                self._jar_top, self._jar_cx)

        n = len(self._particles)
        for _ in range(3):
            for i in range(n):
                for j in range(i + 1, n):
                    self._particles[i].resolve_collision(
                        self._particles[j],
                        self._jar_top, self._jar_x1, self._jar_x2, self._jar_cx)

        for pt in self._particles:
            pt.check_sleep_state()

        if len(self._particles) > 400:
            self._particles.pop(0)

        self.update()

    # ── paint ────────────────────────────────────────────────────
    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.Antialiasing)
        p.setRenderHint(QPainter.SmoothPixmapTransform)

        # เคลียร์ให้โปร่งใส
        p.setCompositionMode(QPainter.CompositionMode_Clear)
        p.fillRect(0, 0, self._win_w, self._win_h, Qt.transparent)
        p.setCompositionMode(QPainter.CompositionMode_SourceOver)

        s = self._scale
        p.save()
        p.scale(s, s)

        cx  = self._jar_cx
        jt  = self._jar_top
        jb  = self._jar_bot
        jx1 = self._jar_cx - JAR_W // 2
        jx2 = self._jar_cx + JAR_W // 2

        # 1. วาดของขวัญภายในโหล (clip)
        p.save()
        jar_clip = QPainterPath()
        jar_clip.moveTo(0, -1000)
        jar_clip.lineTo(JAR_WIN_W, -1000)
        jar_clip.lineTo(JAR_WIN_W, jt + 32)
        jar_clip.lineTo(jx2 - 14, jt + 45)
        jar_clip.lineTo(jx2 - 14, jb - 24)
        jar_clip.quadTo(jx2 - 14, jb - 15, jx2 - 35, jb - 15)
        jar_clip.lineTo(jx1 + 35, jb - 15)
        jar_clip.quadTo(jx1 + 14, jb - 15, jx1 + 14, jb - 24)
        jar_clip.lineTo(jx1 + 14, jt + 45)
        jar_clip.lineTo(0, jt + 32)
        jar_clip.closeSubpath()
        p.setClipPath(jar_clip)
        for pt in self._particles:
            if not pt.overflow:
                pt.draw(p)
        p.restore()

        # 2. วาดตัวโหล
        self._draw_jar(p, cx, jt, jb, jx1, jx2)

        # 3. วาดของขวัญด้านนอก (ล้นออก)
        for pt in self._particles:
            if pt.overflow:
                pt.draw(p)

        p.restore()
        p.end()

    def _draw_jar(self, p, cx, jt, jb, jx1, jx2):
        neck_w = JAR_W - 48
        nx1    = cx - neck_w // 2
        nx2    = cx + neck_w // 2
        neck_h = 32

        body = QPainterPath()
        body.moveTo(nx1, jt + neck_h)
        body.quadTo(jx1 + 3, jt + neck_h + 10, jx1 + 3, jt + neck_h + 40)
        body.lineTo(jx1 + 3, jb - 35)
        body.quadTo(jx1 + 3, jb, jx1 + 35, jb)
        body.lineTo(jx2 - 35, jb)
        body.quadTo(jx2 - 3, jb, jx2 - 3, jb - 35)
        body.lineTo(jx2 - 3, jt + neck_h + 40)
        body.quadTo(jx2 - 3, jt + neck_h + 10, nx2, jt + neck_h)
        body.closeSubpath()

        grad = QLinearGradient(jx1, 0, jx2, 0)
        grad.setColorAt(0.00, QColor(255, 255, 255, 120))
        grad.setColorAt(0.10, QColor(220, 240, 255, 50))
        grad.setColorAt(0.30, QColor(230, 245, 255, 15))
        grad.setColorAt(0.50, QColor(240, 250, 255, 5))
        grad.setColorAt(0.70, QColor(230, 245, 255, 15))
        grad.setColorAt(0.90, QColor(220, 240, 255, 50))
        grad.setColorAt(1.00, QColor(255, 255, 255, 120))
        p.setBrush(QBrush(grad))
        p.setPen(Qt.NoPen)
        p.drawPath(body)

        p.setPen(QPen(QColor(215, 235, 255, 195), 2.5))
        p.setBrush(Qt.NoBrush)
        p.drawPath(body)

        floor_path = QPainterPath()
        floor_path.moveTo(jx1 + 14, jb - 18)
        floor_path.quadTo(cx, jb - 10, jx2 - 14, jb - 18)
        p.setPen(QPen(QColor(235, 245, 255, 130), 1.5))
        p.drawPath(floor_path)

        lid_path = QPainterPath()
        lid_path.addRoundedRect(QRectF(nx1 - 3, jt + 4, neck_w + 6, neck_h - 4), 5, 5)
        lid_grad = QLinearGradient(nx1, jt, nx1, jt + neck_h)
        lid_grad.setColorAt(0.0, QColor(195, 200, 205, 140))
        lid_grad.setColorAt(0.4, QColor(245, 248, 250, 160))
        lid_grad.setColorAt(1.0, QColor(165, 170, 175, 140))
        p.setBrush(QBrush(lid_grad))
        p.setPen(QPen(QColor(145, 150, 155, 150), 2))
        p.drawPath(lid_path)

        p.setPen(QPen(QColor(125, 130, 135, 120), 1.5))
        for i in range(1, 3):
            y = jt + 4 + i * ((neck_h - 4) // 3)
            p.drawLine(nx1 - 1, y, nx2 + 1, y)

        shine = QPainterPath()
        shine.moveTo(jx1 + 18, jt + neck_h + 25)
        shine.quadTo(jx1 + 22, jt + neck_h + 100, jx1 + 19, jb - 45)
        p.setPen(QPen(QColor(255, 255, 255, 140), 6, Qt.SolidLine, Qt.RoundCap))
        p.drawPath(shine)

        shine2 = QPainterPath()
        shine2.moveTo(jx2 - 18, jt + neck_h + 30)
        shine2.quadTo(jx2 - 20, jt + neck_h + 80, jx2 - 19, jt + neck_h + 130)
        p.setPen(QPen(QColor(255, 255, 255, 90), 3, Qt.SolidLine, Qt.RoundCap))
        p.drawPath(shine2)

    # ── drag ─────────────────────────────────────────────────────
    def mousePressEvent(self, e):
        if e.button() == Qt.LeftButton:
            self._oldPos = e.globalPos()

    def mouseMoveEvent(self, e):
        if self._oldPos is not None and e.buttons() == Qt.LeftButton:
            delta = e.globalPos() - self._oldPos
            self.move(self.x() + delta.x(), self.y() + delta.y())
            self._oldPos = e.globalPos()

    def mouseReleaseEvent(self, e):
        self._oldPos = None


# ══════════════════════════════════════════════════════════════════
#  JAR CONTROL PANEL
# ══════════════════════════════════════════════════════════════════
class JarControlPanel(QDialog):
    """แผงควบคุมแก้วโหล — เปิดจากปุ่ม 🫙 ใน MainWindow"""

    def __init__(self, jar: GiftJarOverlay, parent=None):
        super().__init__(parent)
        self.jar      = jar
        self.settings = load_jar_settings()
        self.setWindowTitle("🎁  Gift Jar Control")
        self.setFixedSize(360, 320)
        _ss(self, """
            QDialog  { background:#0a0a1a; color:#FFD700;
                       font-family:'Segoe UI'; }
            QLabel   { color:#cc88ff; }
            QPushButton {
                background:qlineargradient(x1:0,y1:0,x2:1,y2:0,
                    stop:0 #1a0050,stop:0.5 #6600cc,stop:1 #1a0050);
                color:white; font-weight:bold; border-radius:8px;
                padding:10px; font-size:13px; border:none; }
            QPushButton:hover { background:#8800ff; }
            QSlider::groove:horizontal {
                height:6px; background:#333; border-radius:3px; }
            QSlider::handle:horizontal {
                background:#cc44ff; width:18px; height:18px;
                margin:-6px 0; border-radius:9px; }
            QSlider::sub-page:horizontal {
                background:#6600cc; border-radius:3px; }
        """)
        self._build_ui()
        saved = self.settings.get("scale", 100)
        self.slider_scale.setValue(saved)
        self.jar.set_scale(saved)

    def _build_ui(self):
        ml = QVBoxLayout(self)
        ml.setSpacing(14)
        ml.setContentsMargins(24, 20, 24, 20)

        title = QLabel("🎁   GIFT JAR OVERLAY")
        title.setAlignment(Qt.AlignCenter)
        _ss(title, 
            "font-size:15px;font-weight:bold;color:#cc88ff;"
            "padding:8px;border-bottom:1px solid #440088;")
        ml.addWidget(title)

        lbl_s = QLabel(T("jar_ctrl_scale"))
        _ss(lbl_s, "font-size:12px;font-weight:bold;")
        ml.addWidget(lbl_s)

        row_s = QHBoxLayout()
        self.slider_scale = QSlider(Qt.Horizontal)
        self.slider_scale.setLocale(QLocale(QLocale.English, QLocale.UnitedStates))
        self.slider_scale.setRange(40, 200)
        self.slider_scale.setValue(100)
        self.slider_scale.valueChanged.connect(self._on_scale)
        self.lbl_pct = QLabel("100%")
        self.lbl_pct.setFixedWidth(45)
        _ss(self.lbl_pct, "color:#FFD700;")
        row_s.addWidget(self.slider_scale)
        row_s.addWidget(self.lbl_pct)
        ml.addLayout(row_s)

        sep = QFrame()
        sep.setFrameShape(QFrame.HLine)
        _ss(sep, "color:#333;")
        ml.addWidget(sep)

        btn_test = QPushButton(T("jar_ctrl_test"))
        btn_test.clicked.connect(self._test_drop)
        ml.addWidget(btn_test)

        btn_clear = QPushButton(T("jar_ctrl_clear"))
        btn_clear.clicked.connect(self.jar.clear_particles)
        _ss(btn_clear, 
            "background:qlineargradient(x1:0,y1:0,x2:1,y2:0,"
            "stop:0 #300000,stop:0.5 #880000,stop:1 #300000);"
            "color:white;font-weight:bold;border-radius:8px;"
            "padding:10px;font-size:13px;border:none;")
        ml.addWidget(btn_clear)

        self.lbl_count = QLabel(T("jar_ctrl_count", n=0))
        self.lbl_count.setAlignment(Qt.AlignCenter)
        _ss(self.lbl_count, "color:#888;font-size:11px;")
        ml.addWidget(self.lbl_count)

        ct = QTimer(self)
        ct.timeout.connect(self._update_count)
        ct.start(300)

    def _on_scale(self, val):
        self.lbl_pct.setText(f"{val}%")
        self.jar.set_scale(val)
        self.settings["scale"] = val
        save_jar_settings(self.settings)

    def _test_drop(self):
        for i in range(5):
            QTimer.singleShot(i * 100, self.jar.add_gift)

    def _update_count(self):
        self.lbl_count.setText(T("jar_ctrl_count", n=self.jar.particle_count()))


# ══════════════════════════════════════════════════════════════════
#  URL / Avatar helpers
# ══════════════════════════════════════════════════════════════════
def extract_all_http_urls(x):
    out = []

    def _add(u):
        u = (u or "").strip()
        if not u: return
        if u.startswith("//"): u = "https:" + u
        if u.startswith("http://") or u.startswith("https://"):
            if u not in out: out.append(u)

    if not x: return out
    if isinstance(x, str):
        s = x.strip()
        if not s: return out
        if s.startswith("//"): _add("https:" + s); return out
        if s.startswith("http://") or s.startswith("https://"): _add(s); return out
        for m in _URL_RE.findall(s): _add(m)
        return out
    if isinstance(x, (list, tuple, set)):
        for item in x:
            for u in extract_all_http_urls(item): _add(u)
        return out
    if isinstance(x, dict):
        for k in ("m_urls", "urls", "url_list", "urlList", "url",
                  "profilePictureUrl", "profile_picture", "picture", "avatar", "pic"):
            if k in x and x.get(k):
                for u in extract_all_http_urls(x.get(k)): _add(u)
        for v in x.values():
            for u in extract_all_http_urls(v): _add(u)
        return out
    for attr in ("m_urls", "urls", "url_list", "urlList", "url", "uri", "m_uri", "profilePictureUrl"):
        try:
            v = getattr(x, attr, None)
        except Exception: v = None
        if v:
            for u in extract_all_http_urls(v): _add(u)
    try:
        if hasattr(x, "to_pydict"):
            d = x.to_pydict()
            for u in extract_all_http_urls(d): _add(u)
    except Exception: pass
    try:
        s = str(x)
        for m in _URL_RE.findall(s): _add(m)
    except Exception: pass
    return out


# ─── Voice ───────────────────────────────────────────────────────
VOICE_MODE_OFF       = "off"
VOICE_MODE_THAI      = "thai"
VOICE_MODE_THAI_SOFT = "thai_soft"
EDGE_VOICE_THAI      = "th-TH-NiwatNeural"
EDGE_VOICE_THAI_SOFT = "th-TH-PremwadeeNeural"

pydirectinput.PAUSE    = 0
pydirectinput.FAILSAFE = False
KEY_DELAY          = 0.13
REPEAT_DELAY       = 0.0
CHAT_RATE_LIMIT_SEC = 1.0
CHAT_MAX_CHARS     = 120

CHAT_FILTER_ENABLED = True
CHAT_FILTER_MODE    = "skip"
CHAT_BADWORDS = [
    "ควย","หี","เย็ด","เอากัน","xxx","18+","เงี่ยน","สัส","สัด","เหี้ย","เชี่ย",
    "อีเหี้ย","ไอเหี้ย","ห่า","ฉิบหาย","แม่ง","มึง","กู","ควาย","อีควาย","ไอควาย",
    "โง่","อีโง่","ไอโง่","อีดอก","ดอกทอง","กะหรี่","ชาติหมา","สันดาน","ส้นตีน",
    "ตีน","หน้าหี","หมอย","ปิ","กี","อม","แตด","จิ๋ม","จู๋","คิ๊","กี๊","เย็ดแม่",
    "ยัดแม่","กระเด้า","ชักว่าว","เบ็ด","เลียหี","อมควย","หน้าควย","รูตูด","รุมโทรม",
    "ขายตัว","แมงดา","ไซด์ไลน์","นัดยิ้ม","กินตับ","หน้าตัวเมีย","สวะ","ปัญญาอ่อน",
    "เอ๋อ","ตุ๊ด","แต๋ว","อีแอบ",
    "fuck","shit","bitch","asshole","dick","pussy","cunt","whore","slut","motherfucker",
    "cock","clit","blowjob","handjob","cum","tit","boobs","bastard","idiot","porn","hentai",
    "nigger","faggot",
]


def _build_loose_pattern(word):
    w = (word or "").strip()
    if not w: return None
    base = re.sub(r"[^\w\u0E00-\u0E7F]+", "", w, flags=re.UNICODE)
    if not base: return None
    try:
        return re.compile("".join(re.escape(ch) + r"{1,2}" for ch in base),
                          flags=re.IGNORECASE | re.UNICODE)
    except Exception: return None


_BAD_PATTERNS = [p for p in (_build_loose_pattern(w) for w in CHAT_BADWORDS) if p]


def sanitize_chat_for_tts(msg):
    text = (msg or "").strip()
    if not text: return "", False
    for pat in _BAD_PATTERNS:
        if pat.search(text): return "", True
    return text, False


def norm_name(s):
    return (s or "").strip().lower()


def normalize_map(m):
    out = {}
    for k, v in (m or {}).items():
        out[norm_name(k)] = (v or "").strip()
    return out


def _pick_display_name(user_obj):
    if not user_obj: return "Unknown"
    for k in ("name", "displayName", "display_name", "nickname", "nick_name", "unique_id", "uniqueId"):
        try: v = getattr(user_obj, k, None)
        except Exception: v = None
        if v:
            s = str(v).strip()
            if s: return s
    try:
        if hasattr(user_obj, "to_pydict"):
            d = user_obj.to_pydict()
            for k in ("name", "displayName", "display_name", "nickname", "uniqueId", "unique_id"):
                v = d.get(k)
                if v: return str(v).strip()
    except Exception: pass
    return "Unknown"


def _pick_avatar(user_obj):
    if not user_obj: return ""
    for k in ("profilePictureUrl", "profile_picture", "profilePicture", "picture",
               "avatar", "avatar_thumb", "avatarThumb", "pic",
               "m_urls", "urls", "url_list", "urlList", "url"):
        try: v = getattr(user_obj, k, None)
        except Exception: v = None
        if v: return v
    try:
        if hasattr(user_obj, "to_pydict"):
            d = user_obj.to_pydict()
            for k in ("profilePictureUrl", "profile_picture", "profilePicture", "picture",
                       "avatar", "avatarThumb", "avatar_thumb", "pic",
                       "m_urls", "urls", "url_list", "urlList", "url"):
                if k in d and d.get(k): return d.get(k)
    except Exception: pass
    return ""


# ══════════════════════════════════════════════════════════════════
#  LANGUAGE STRINGS  (TH / EN)
# ══════════════════════════════════════════════════════════════════
LANG_TH = {
    "sec_connect":            "เชื่อมต่อ TikTok LIVE",
    "sec_overlays":           "หน้าจอซ้อน (OBS)",
    "sec_jar":                "โหลของขวัญ",
    "header_sub":             "TikTok LIVE → คีย์ลัดในเกม Roblox",
    "clear_logs":             "ล้าง",
    "main_title":             "PTV – เกมมาหาเงิน",
    "preset_label":           "Preset:",
    "settings_btn":           "⚙️  ตั้งค่า",
    "voice_label":            "🔊 เสียงอ่านแชท:",
    "connect_btn":            "เชื่อมต่อ",
    "disconnect_btn":         "ตัดการเชื่อมต่อ",
    "username_placeholder":   "@ชื่อผู้ใช้",
    "top_likers_btn":         "❤️  ยอดกด Like",
    "top_gifters_btn":        "🪙  ยอดส่งของขวัญ",
    "win_counter_btn":        "🏆  นับ Win",
    "jar_btn":                "🫙  แก้วโหล",
    "status_ready":           "⚪ พร้อมเชื่อมต่อ",
    "logs_label":             "📋 บันทึกเหตุการณ์:",
    "tip":                    "ทิป: คลิกหน้าต่างเกมให้ Active ก่อน เพื่อให้รับคีย์ชัวร์",
    "lang_btn":               "EN",
    "voice_off":              "ปิดเสียงอ่าน",
    "voice_thai_m":           "ไทย ชาย (Niwat)",
    "voice_thai_f":           "ไทย หญิง (Premwadee)",
    "settings_title":         "⚙️  ตั้งค่า Preset",
    "import_btn":             "📥 นำเข้า JSON",
    "export_btn":             "📤 ส่งออก JSON",
    "save_btn":               "💾 บันทึกทั้งหมด",
    "presets_label":          "📋 รายการ Preset",
    "add_preset_btn":         "+ เพิ่ม",
    "del_preset_btn":         "ลบ",
    "dup_preset_btn":         "⧉ ทำสำเนา",
    "preset_name_label":      "ชื่อ Preset:",
    "preset_name_placeholder":"ชื่อ Preset",
    "tab_gift":               "🎁 แผนที่ของขวัญ",
    "tab_follow_like":        "❤️ ติดตาม / ถูกใจ",
    "gift_col_name":          "ชื่อของขวัญ",
    "gift_col_key":           "คีย์ที่กด",
    "add_gift_btn":           "+ เพิ่มของขวัญ",
    "follow_section":         "👤  การติดตาม (Follow)",
    "follow_key_label":       "คีย์ที่กด:",
    "follow_enabled_chk":     "เปิดใช้งาน Follow Action",
    "like_enabled_chk":       "เปิดใช้งาน Like Action",
    "follow_once_chk":        "กดได้แค่ครั้งเดียวต่อผู้ใช้  (ถ้าผู้ใช้เดิมติดตามซ้ำจะถูกข้าม)",
    "like_section":           "❤️  การกดถูกใจ (Like)",
    "like_key_label":         "คีย์ที่กด:",
    "like_target_label":      "กดทุกกี่ครั้ง:",
    "like_target_unit":       "ครั้ง",
    "status_ready_settings":  "พร้อมใช้งาน",
    "confirm_del_title":      "ยืนยันการลบ",
    "confirm_del_msg":        "ลบ '{name}' ใช่ไหม?",
    "cant_del_title":         "ลบไม่ได้",
    "cant_del_msg":           "ต้องมี Preset อย่างน้อย 1 รายการ",
    "import_title":           "นำเข้า Presets JSON",
    "import_confirm":         "พบ {n} Preset\n\n[Yes] = แทนที่ทั้งหมด\n[No] = เพิ่มต่อท้าย\n[Cancel] = ยกเลิก",
    "import_bad_format":      'รูปแบบ JSON ไม่ถูกต้อง ต้องเป็น list หรือ {"presets": [...]}',
    "import_empty":           "ไม่พบ Preset ที่ถูกต้องใน JSON",
    "import_fail_title":      "นำเข้าล้มเหลว",
    "export_title":           "ส่งออก Presets JSON",
    "export_ok_title":        "ส่งออกสำเร็จ",
    "export_ok_msg":          "บันทึกไฟล์ที่:\n{path}",
    "export_fail_title":      "ส่งออกล้มเหลว",
    "preset_hint":            "Preset {n}: ติดตาม → {fseq} ({fonce})  |  ถูกใจ {ltarget} → {lseq}  |  ของขวัญ: {ngifts}",
    "follow_once_yes":        "✅ ครั้งเดียว/คน",
    "follow_once_no":         "♾️ ทุกครั้ง",
    "log_preset_change":      "🔧 เปลี่ยน Preset: {name}",
    "log_presets_saved":      "✅ บันทึก {n} Preset สำเร็จ",
    "log_voice_change":       "🔊 เปลี่ยนเสียง: {label}",
    "log_disconnect_req":     "⏹️ ตัดการเชื่อมต่อ",
    "log_no_username":        "❌ กรุณาใส่ชื่อผู้ใช้ก่อน",
    "gift_no_image":          "ไม่มีรูป",
    "gift_search_placeholder":"🔍 ค้นหาของขวัญ...",
    "gift_select_placeholder":"-- เลือกของขวัญ --",
    "gift_custom_label":      "หรือพิมพ์เอง:",
    "gift_picker_title":      "🎁  เลือกของขวัญ",
    "gift_picker_not_sel":    "ยังไม่ได้เลือก",
    "gift_picker_selected":   "เลือก:",
    "gift_picker_confirm":    "✅  ยืนยัน",
    "gift_picker_cancel":     "ยกเลิก",
    "gift_picker_click":      "คลิกเพื่อเลือกของขวัญ…",
    "win_label":              "Win",
    "win_settings_title":     "🏆  ตั้งค่า Win Counter",
    "win_hk_section":         "⌨️  คีย์ลัด & สูงสุด",
    "win_hk_up":              "คีย์ Win +1:",
    "win_hk_dn":              "คีย์ Win -1:",
    "win_hk_rst":             "คีย์รีเซ็ต:",
    "win_max":                "Win สูงสุด:",
    "win_appear_section":     "🎨  รูปลักษณ์ Overlay",
    "win_bg_label":           "พื้นหลัง:",
    "win_text_color":         "สีตัวอักษร:",
    "win_font_size":          "ขนาดตัวอักษร:",
    "win_chroma_hint":        "(Chroma → OBS Color Key #00ff00)",
    "win_rules_section":      "🎁  ของขวัญ → กฎ Win",
    "win_add_rule":           "+ เพิ่มกฎ",
    "win_col_gift":           "ชื่อของขวัญ + ราคา",
    "win_col_delta":          "Win +/-",
    "win_col_key":            "คีย์ (เสริม)",
    "win_save":               "💾  บันทึก",
    "win_days_left":          "วันที่เหลือ",
    "win_btn_plus":           "+",
    "win_btn_minus":          "−",
    "win_btn_reset":          "รีเซ็ต",
    "win_btn_settings":       "ตั้งค่า",
    "win_pick_gift":          "คลิกเพื่อเลือก…",
    "win_key_label":          "คีย์:",
    "bg_black":               "ดำทึบ",
    "bg_navy":                "น้ำเงินเข้ม",
    "bg_dark":                "เทาเข้ม",
    "bg_darkred":             "แดงเข้ม",
    "bg_chroma":              "โปร่งใส (Chroma)",
    "tc_white":               "ขาว",
    "tc_yellow":              "เหลือง",
    "tc_red":                 "แดง",
    "tc_cyan":                "ฟ้า",
    "tc_green":               "เขียว",
    "jar_ctrl_title":         "🫙  Gift Jar",
    "jar_ctrl_scale":         "🔲  ปรับขนาดแก้วโหล",
    "jar_ctrl_test":          "🎁  ทดสอบ Drop (x5)",
    "jar_ctrl_clear":         "🗑  ล้างทั้งหมด",
    "jar_ctrl_count":         "ของขวัญบนจอ: {n} ชิ้น",
    "win_counter_section":    "🏆  Win Counter",
    "win_max_label":          "Win สูงสุด:",
    "win_hotkey_up":          "Hotkey +1:",
    "win_hotkey_dn":          "Hotkey -1:",
    "win_hotkey_rst":         "Hotkey Reset:",
    "gift_win_rules":         "🎁  ของขวัญ → กฎ Win",
    "add_win_rule":           "+ เพิ่มกฎ",
    "win_col_header_gift":    "ของขวัญ",
    "win_col_header_act":     "การกระทำ",
}

LANG_EN = {
    "sec_connect":            "Connect TikTok LIVE",
    "sec_overlays":           "Overlays (OBS)",
    "sec_jar":                "Gift Jar",
    "header_sub":             "TikTok LIVE → Roblox key presses",
    "clear_logs":             "Clear",
    "main_title":             "PTV – Game To Earn",
    "preset_label":           "Preset:",
    "settings_btn":           "⚙️  Settings",
    "voice_label":            "🔊 TTS Voice:",
    "connect_btn":            "CONNECT",
    "disconnect_btn":         "DISCONNECT",
    "username_placeholder":   "@username",
    "top_likers_btn":         "❤️  TOP LIKERS",
    "top_gifters_btn":        "🪙  TOP GIFTERS",
    "win_counter_btn":        "🏆  WIN COUNTER",
    "jar_btn":                "🫙  GIFT JAR",
    "status_ready":           "⚪ Ready to connect",
    "logs_label":             "📋 Logs:",
    "tip":                    "Tip: Click the game window first so key presses register correctly",
    "lang_btn":               "TH",
    "voice_off":              "Off",
    "voice_thai_m":           "Thai Male (Niwat)",
    "voice_thai_f":           "Thai Female (Premwadee)",
    "settings_title":         "⚙️  Preset Settings",
    "import_btn":             "📥 Import JSON",
    "export_btn":             "📤 Export JSON",
    "save_btn":               "💾 Save All",
    "presets_label":          "📋 Presets",
    "add_preset_btn":         "+ Add",
    "del_preset_btn":         "Delete",
    "dup_preset_btn":         "⧉ Duplicate",
    "preset_name_label":      "Preset name:",
    "preset_name_placeholder":"Preset name",
    "tab_gift":               "🎁 Gift Map",
    "tab_follow_like":        "❤️ Follow / Like",
    "gift_col_name":          "Gift Name",
    "gift_col_key":           "Key to press",
    "add_gift_btn":           "+ Add Gift",
    "follow_section":         "👤  Follow Action",
    "follow_key_label":       "Key to press:",
    "follow_enabled_chk":     "Enable Follow Action",
    "like_enabled_chk":       "Enable Like Action",
    "follow_once_chk":        "Press only once per user  (duplicate follows are skipped)",
    "like_section":           "❤️  Like Action",
    "like_key_label":         "Key to press:",
    "like_target_label":      "Press every N likes:",
    "like_target_unit":       "likes",
    "status_ready_settings":  "Ready",
    "confirm_del_title":      "Confirm Delete",
    "confirm_del_msg":        "Delete '{name}'?",
    "cant_del_title":         "Cannot Delete",
    "cant_del_msg":           "At least 1 preset must remain",
    "import_title":           "Import Presets JSON",
    "import_confirm":         "Found {n} Preset(s)\n\n[Yes] = Replace all\n[No] = Append\n[Cancel] = Cancel",
    "import_bad_format":      'Invalid JSON format – must be a list or {"presets": [...]}',
    "import_empty":           "No valid presets found in JSON",
    "import_fail_title":      "Import Failed",
    "export_title":           "Export Presets JSON",
    "export_ok_title":        "Export Successful",
    "export_ok_msg":          "File saved at:\n{path}",
    "export_fail_title":      "Export Failed",
    "preset_hint":            "Preset {n}: Follow → {fseq} ({fonce})  |  Like every {ltarget} → {lseq}  |  Gifts: {ngifts}",
    "follow_once_yes":        "✅ Once/User",
    "follow_once_no":         "♾️ Every time",
    "log_preset_change":      "🔧 Preset changed: {name}",
    "log_presets_saved":      "✅ {n} preset(s) saved",
    "log_voice_change":       "🔊 Voice changed: {label}",
    "log_disconnect_req":     "⏹️ Disconnect requested",
    "log_no_username":        "❌ Enter a username first",
    "gift_no_image":          "No image",
    "gift_search_placeholder":"🔍 Search gifts...",
    "gift_select_placeholder":"-- Select a gift --",
    "gift_custom_label":      "Or type manually:",
    "gift_picker_title":      "🎁  Select Gift",
    "gift_picker_not_sel":    "None selected",
    "gift_picker_selected":   "Selected:",
    "gift_picker_confirm":    "✅  Select",
    "gift_picker_cancel":     "Cancel",
    "gift_picker_click":      "Click to choose gift…",
    "win_label":              "Win",
    "win_settings_title":     "🏆  Win Counter Settings",
    "win_hk_section":         "⌨️  Hotkeys & Max",
    "win_hk_up":              "Win +1 key:",
    "win_hk_dn":              "Win -1 key:",
    "win_hk_rst":             "Reset key:",
    "win_max":                "Max Win:",
    "win_appear_section":     "🎨  Overlay Appearance",
    "win_bg_label":           "Background:",
    "win_text_color":         "Text color:",
    "win_font_size":          "Font size:",
    "win_chroma_hint":        "(Chroma → OBS Color Key #00ff00)",
    "win_rules_section":      "🎁  Gift Win Rules",
    "win_add_rule":           "+ Add Rule",
    "win_col_gift":           "Gift Name + Price",
    "win_col_delta":          "Win +/-",
    "win_col_key":            "Key (opt)",
    "win_save":               "💾  Save",
    "win_days_left":          "days remaining",
    "win_btn_plus":           "+",
    "win_btn_minus":          "−",
    "win_btn_reset":          "Reset",
    "win_btn_settings":       "Settings",
    "win_pick_gift":          "Click to choose…",
    "win_key_label":          "Key:",
    "bg_black":               "Solid Black",
    "bg_navy":                "Dark Navy",
    "bg_dark":                "Dark Gray",
    "bg_darkred":             "Dark Red",
    "bg_chroma":              "Transparent (Chroma)",
    "tc_white":               "White",
    "tc_yellow":              "Yellow",
    "tc_red":                 "Red",
    "tc_cyan":                "Cyan",
    "tc_green":               "Green",
    "jar_ctrl_title":   "🫙  Gift Jar",
    "jar_ctrl_scale":   "🔲  Adjust Jar Scale",
    "jar_ctrl_test":   "🎁   Test Drop Gifts (x5)",
    "jar_ctrl_clear":   "🗑   Clear All Gifts",
    "jar_ctrl_count":   "Gifts on screen: {n}",
    "win_counter_section":   "🏆  Win Counter",
    "win_max_label":   "Max Win:",
    "win_bg_label":   "Background:",
    "win_text_color":   "Text color:",
    "win_font_size":   "Font size:",
    "win_hotkey_up":   "Hotkey +1:",
    "win_hotkey_dn":   "Hotkey -1:",
    "win_hotkey_rst":   "Hotkey Reset:",
    "gift_win_rules":   "🎁  Gift Win Rules",
    "add_win_rule":   "+ Add Rule",
    "win_col_header_gift":   "Gift",
    "win_col_header_act":   "Action",
}
_LANG = LAUNCHER_LANG

# ══════════════════════════════════════════════════════════════════
#  LANG FILIPINO
# ══════════════════════════════════════════════════════════════════
LANG_FIL = {
    "main_title":  "PTV – Kumita sa Laro",
    "preset_label":  "Preset:",
    "settings_btn":  "⚙️  Mga Setting",
    "voice_label":  "🔊 Boses ng TTS:",
    "connect_btn":  "KUMONEKTA",
    "disconnect_btn":  "IDISKONEKTA",
    "username_placeholder":  "@username",
    "top_likers_btn":  "❤️  TOP LIKERS",
    "top_gifters_btn":  "🪙  TOP GIFTERS",
    "win_counter_btn":  "🏆  WIN COUNTER",
    "jar_btn":  "🫙  GIFT JAR",
    "status_ready":  "⚪ Handa nang kumonekta",
    "logs_label":  "📋 Mga Log:",
    "tip":  "Tip: I-click muna ang window ng laro bago mag-stream",
    "lang_btn":  "TL",
    "voice_off":  "Patayin",
    "voice_thai_m":  "Thai Lalaki (Niwat)",
    "voice_thai_f":  "Thai Babae (Premwadee)",
    "settings_title":  "⚙️  Mga Setting ng Preset",
    "import_btn":  "📥 I-import JSON",
    "export_btn":  "📤 I-export JSON",
    "save_btn":  "💾 I-save Lahat",
    "presets_label":  "📋 Mga Preset",
    "add_preset_btn":  "+ Magdagdag",
    "del_preset_btn":  "Burahin",
    "dup_preset_btn":  "⧉ Kopyahin",
    "preset_name_label":  "Pangalan ng Preset:",
    "preset_name_placeholder":  "Pangalan ng Preset",
    "tab_gift":  "🎁 Gift Map",
    "tab_follow_like":  "❤️ Sundan / Like",
    "gift_col_name":  "Pangalan ng Regalo",
    "gift_col_key":  "Key na pipindutin",
    "add_gift_btn":  "+ Magdagdag ng Regalo",
    "follow_section":  "👤  Aksyon sa Pagsunod",
    "follow_key_label":  "Key na pipindutin:",
    "follow_enabled_chk":  "I-enable ang Follow Action",
    "like_enabled_chk":  "I-enable ang Like Action",
    "follow_once_chk":  "Pindutin nang isang beses bawat user (paulit-ulit ay laktawan)",
    "like_section":  "❤️  Aksyon sa Like",
    "like_key_label":  "Key na pipindutin:",
    "like_target_label":  "Pindutin bawat N likes:",
    "like_target_unit":  "likes",
    "status_ready_settings":  "Handa",
    "confirm_del_title":  "Kumpirmahin ang Pagtanggal",
    "confirm_del_msg":  "Tanggalin ang '{name}'?",
    "cant_del_title":  "Hindi Matanggal",
    "cant_del_msg":  "Dapat may kahit isang preset",
    "import_title":  "I-import ang Presets JSON",
    "import_confirm":  "Nahanap ang {n} Preset(s)\n\n[Yes]=Palitan  [No]=Idagdag  [Cancel]=Kanselahin",
    "import_bad_format":  "Mali ang format ng JSON - dapat list o {\"presets\":[...]}",
    "import_empty":  "Walang nahanap na valid na preset sa JSON",
    "import_fail_title":  "Nabigo ang Import",
    "export_title":  "I-export ang Presets JSON",
    "export_ok_title":  "Matagumpay ang Export",
    "export_ok_msg":  "Na-save ang file sa:\n{path}",
    "export_fail_title":  "Nabigo ang Export",
    "preset_hint":  "Preset {n}: Sundan→{fseq}({fonce}) | Like {ltarget}→{lseq} | Regalo:{ngifts}",
    "follow_once_yes":  "✅ Isang Beses/User",
    "follow_once_no":  "♾️ Bawat Oras",
    "log_preset_change":  "🔧 Binago ang Preset: {name}",
    "log_presets_saved":  "✅ Na-save ang {n} preset(s)",
    "log_voice_change":  "🔊 Binago ang boses: {label}",
    "log_disconnect_req":  "⏹️ Hiniling na idiskonekta",
    "log_no_username":  "❌ Maglagay muna ng username",
    "gift_no_image":  "Walang larawan",
    "gift_search_placeholder":  "🔍 Maghanap ng regalo...",
    "gift_select_placeholder":  "-- Pumili ng regalo --",
    "gift_custom_label":  "O mag-type nang mano-mano:",
    "gift_picker_title":  "🎁  Pumili ng Regalo",
    "gift_picker_not_sel":  "Wala pang napili",
    "gift_picker_selected":  "Napili:",
    "gift_picker_confirm":  "✅  Piliin",
    "gift_picker_cancel":  "Kanselahin",
    "gift_picker_click":  "I-click para pumili ng regalo…",
    "win_label":  "Panalo",
    "win_settings_title":  "🏆  Mga Setting ng Win Counter",
    "win_hk_section":  "⌨️  Mga Hotkey at Max",
    "win_hk_up":  "Key Win +1:",
    "win_hk_dn":  "Key Win -1:",
    "win_hk_rst":  "Key Reset:",
    "win_max":  "Max Win:",
    "win_appear_section":  "🎨  Hitsura ng Overlay",
    "win_bg_label":  "Background:",
    "win_text_color":  "Kulay ng teksto:",
    "win_font_size":  "Laki ng font:",
    "win_chroma_hint":  "(Chroma → OBS Color Key #00ff00)",
    "win_rules_section":  "🎁  Regalo → Mga Patakaran",
    "win_add_rule":  "+ Magdagdag ng Patakaran",
    "win_col_gift":  "Pangalan ng Regalo + Presyo",
    "win_col_delta":  "Win +/-",
    "win_col_key":  "Key (opsyonal)",
    "win_save":  "💾  I-save",
    "win_days_left":  "araw na natitira",
    "win_btn_plus":  "+",
    "win_btn_minus":  "−",
    "win_btn_reset":  "I-reset",
    "win_btn_settings":  "Mga Setting",
    "win_pick_gift":  "I-click para pumili…",
    "win_key_label":  "Key:",
    "bg_black":  "Solid Black",
    "bg_navy":  "Dark Navy",
    "bg_dark":  "Dark Gray",
    "bg_darkred":  "Dark Red",
    "bg_chroma":  "Transparent (Chroma)",
    "tc_white":  "Puti",
    "tc_yellow":  "Dilaw",
    "tc_red":  "Pula",
    "tc_cyan":  "Asul-berde",
    "tc_green":  "Berde",
    "jar_ctrl_count":       "Mga regalo sa screen: {n}",
    "jar_ctrl_scale":       "🔲  Ayusin ang sukat",
    "jar_ctrl_test":        "🎁  Subok Drop (x5)",
    "win_counter_section":  "🏆  Win Counter",
    "win_max_label":        "Max Win:",
    "win_hotkey_up":        "Hotkey +1:",
    "win_hotkey_dn":        "Hotkey -1:",
    "win_hotkey_rst":       "Hotkey Reset:",
    "gift_win_rules":       "🎁  Regalo → Win Rules",
    "add_win_rule":         "+ Magdagdag ng Panuntunan",
    "win_col_header_gift":  "Pangalan ng Regalo",
    "win_col_header_act":   "Aksyon",
    "jar_ctrl_clear":  "🗑   Linisin ang Lahat",
}

# ══════════════════════════════════════════════════════════════════
#  LANG RUSSIAN
# ══════════════════════════════════════════════════════════════════
LANG_RU = {
    "main_title":  "PTV – Зарабатывай в игре",
    "preset_label":  "Пресет:",
    "settings_btn":  "⚙️  Настройки",
    "voice_label":  "🔊 Голос TTS:",
    "connect_btn":  "ПОДКЛЮЧИТЬ",
    "disconnect_btn":  "ОТКЛЮЧИТЬ",
    "username_placeholder":  "@имя_пользователя",
    "top_likers_btn":  "❤️  ТОП ЛАЙКОВ",
    "top_gifters_btn":  "🪙  ТОП ПОДАРКОВ",
    "win_counter_btn":  "🏆  СЧЁТЧИК",
    "jar_btn":  "🫙  БАНКА",
    "status_ready":  "⚪ Готово к подключению",
    "logs_label":  "📋 Журнал:",
    "tip":  "Совет: Сначала нажмите на окно игры",
    "lang_btn":  "RU",
    "voice_off":  "Выключено",
    "voice_thai_m":  "Тайский мужской (Niwat)",
    "voice_thai_f":  "Тайский женской (Premwadee)",
    "settings_title":  "⚙️  Настройки пресета",
    "import_btn":  "📥 Импорт JSON",
    "export_btn":  "📤 Экспорт JSON",
    "save_btn":  "💾 Сохранить всё",
    "presets_label":  "📋 Пресеты",
    "add_preset_btn":  "+ Добавить",
    "del_preset_btn":  "Удалить",
    "dup_preset_btn":  "⧉ Дублировать",
    "preset_name_label":  "Название пресета:",
    "preset_name_placeholder":  "Название пресета",
    "tab_gift":  "🎁 Карта подарков",
    "tab_follow_like":  "❤️ Подписка / Лайк",
    "gift_col_name":  "Название подарка",
    "gift_col_key":  "Клавиша",
    "add_gift_btn":  "+ Добавить подарок",
    "follow_section":  "👤  Подписка",
    "follow_key_label":  "Клавиша:",
    "follow_enabled_chk":  "Включить Follow Action",
    "like_enabled_chk":  "Включить Like Action",
    "follow_once_chk":  "Нажать один раз на пользователя (повторные пропускаются)",
    "like_section":  "❤️  Лайк",
    "like_key_label":  "Клавиша:",
    "like_target_label":  "Нажать каждые N лайков:",
    "like_target_unit":  "лайков",
    "status_ready_settings":  "Готово",
    "confirm_del_title":  "Подтвердить удаление",
    "confirm_del_msg":  "Удалить '{name}'?",
    "cant_del_title":  "Нельзя удалить",
    "cant_del_msg":  "Должен остаться хотя бы 1 пресет",
    "import_title":  "Импорт Presets JSON",
    "import_confirm":  "Найдено {n} пресет(ов)\n\n[Yes]=Заменить  [No]=Добавить  [Cancel]=Отмена",
    "import_bad_format":  "Неверный формат JSON",
    "import_empty":  "Не найдено валидных пресетов",
    "import_fail_title":  "Ошибка импорта",
    "export_title":  "Экспорт Presets JSON",
    "export_ok_title":  "Экспорт выполнен",
    "export_ok_msg":  "Файл сохранён:\n{path}",
    "export_fail_title":  "Ошибка экспорта",
    "preset_hint":  "Preset {n}: Подписка→{fseq}({fonce}) | Лайк {ltarget}→{lseq} | Подарки:{ngifts}",
    "follow_once_yes":  "✅ Один раз/пользователь",
    "follow_once_no":  "♾️ Каждый раз",
    "log_preset_change":  "🔧 Сменён пресет: {name}",
    "log_presets_saved":  "✅ Сохранено {n} пресет(ов)",
    "log_voice_change":  "🔊 Голос изменён: {label}",
    "log_disconnect_req":  "⏹️ Запрос на отключение",
    "log_no_username":  "❌ Сначала введите имя пользователя",
    "gift_no_image":  "Нет изображения",
    "gift_search_placeholder":  "🔍 Поиск подарка...",
    "gift_select_placeholder":  "-- Выберите подарок --",
    "gift_custom_label":  "Или введите вручную:",
    "gift_picker_title":  "🎁  Выбрать подарок",
    "gift_picker_not_sel":  "Не выбрано",
    "gift_picker_selected":  "Выбрано:",
    "gift_picker_confirm":  "✅  Выбрать",
    "gift_picker_cancel":  "Отмена",
    "gift_picker_click":  "Нажмите для выбора…",
    "win_label":  "Победа",
    "win_settings_title":  "🏆  Настройки Win Counter",
    "win_hk_section":  "⌨️  Горячие клавиши и Max",
    "win_hk_up":  "Клавиша Win +1:",
    "win_hk_dn":  "Клавиша Win -1:",
    "win_hk_rst":  "Клавиша сброса:",
    "win_max":  "Макс. побед:",
    "win_appear_section":  "🎨  Вид Overlay",
    "win_bg_label":  "Фон:",
    "win_text_color":  "Цвет текста:",
    "win_font_size":  "Размер шрифта:",
    "win_chroma_hint":  "(Chroma → OBS Color Key #00ff00)",
    "win_rules_section":  "🎁  Подарок → Правила Win",
    "win_add_rule":  "+ Добавить правило",
    "win_col_gift":  "Название + Цена",
    "win_col_delta":  "Win +/-",
    "win_col_key":  "Клавиша (opt)",
    "win_save":  "💾  Сохранить",
    "win_days_left":  "дней осталось",
    "win_btn_plus":  "+",
    "win_btn_minus":  "−",
    "win_btn_reset":  "Сброс",
    "win_btn_settings":  "Настройки",
    "win_pick_gift":  "Нажмите для выбора…",
    "win_key_label":  "Клавиша:",
    "bg_black":  "Чёрный",
    "bg_navy":  "Тёмно-синий",
    "bg_dark":  "Тёмно-серый",
    "bg_darkred":  "Тёмно-красный",
    "bg_chroma":  "Прозрачный (Chroma)",
    "tc_white":  "Белый",
    "tc_yellow":  "Жёлтый",
    "tc_red":  "Красный",
    "tc_cyan":  "Голубой",
    "tc_green":  "Зёленый",
    "jar_ctrl_count":       "Подарков на экране: {n}",
    "jar_ctrl_scale":       "🔲  Размер банки",
    "jar_ctrl_test":        "🎁  Тест Drop (x5)",
    "win_counter_section":  "🏆  Счётчик побед",
    "win_max_label":        "Макс. побед:",
    "win_hotkey_up":        "Горячая клавиша +1:",
    "win_hotkey_dn":        "Горячая клавиша -1:",
    "win_hotkey_rst":       "Клавиша сброса:",
    "gift_win_rules":       "🎁  Подарок → Win Rules",
    "add_win_rule":         "+ Добавить правило",
    "win_col_header_gift":  "Название подарка",
    "win_col_header_act":   "Действие",
    "jar_ctrl_clear":  "🗑   Очистить всё",
}

# ── รายการภาษาที่รองรับ
LANG_LIST = [
    ("TH",  "\U0001f1f9\U0001f1ed \u0e44\u0e17\u0e22",    LANG_TH),
    ("EN",  "\U0001f1ec\U0001f1e7 English",  LANG_EN),
    ("FIL", "\U0001f1f5\U0001f1ed Filipino", LANG_FIL),
    ("RU",  "\U0001f1f7\U0001f1fa \u0420\u0443\u0441\u0441\u043a\u0438\u0439", LANG_RU),
]


def T(key, **kw):
    current = next((d for code, _, d in LANG_LIST if code == _LANG), LANG_EN)
    s = current.get(key, LANG_EN.get(key, key))
    return s.format(**kw) if kw else s


def _next_lang():
    codes = [c for c, _, _ in LANG_LIST]
    idx   = codes.index(_LANG) if _LANG in codes else 0
    return codes[(idx + 1) % len(codes)]


def _lang_button_label():
    """ป้ายปุ่มภาษา = flag + code ของภาษาปัจจุบัน"""
    for c, lbl, _ in LANG_LIST:
        if c == _LANG:
            return lbl
    return _LANG


# ══════════════════════════════════════════════════════════════════
#  DEFAULT PRESETS
# ══════════════════════════════════════════════════════════════════
DEFAULT_PRESETS = [
    {
        "name": "per box map",
        "gift_map": {
            "hand hearts": "m", "hand heart": "m", "perfume": "n",
            "friendship necklace": "b", "bff necklace": "b", "rose": "v",
            "confetti": "u", "doughnut": "y", "rosa": "t", "tiktok": "r",
            "heart me": "p", "flower garland": "[", "baby hippo": "]",
            "little hippo": "]", "fruit friends": "q", "corgi": "c",
            "motorcycle": "q", "rhythmic bear": "c",
        },
        "follow_seq": "r", "follow_once": True,
        "like_seq": "v", "like_target": 100,
    },
    {
        "name": "sorting box map",
        "gift_map": {
            "heart me": "h", "rose": "v", "friendship necklace": "tt",
            "bff necklace": "tt", "rosa": "b", "perfume": "n",
            "hand hearts": "m", "hand heart": "m", "finger heart": "m",
            "gg": "rr", "super gg": "u", "doughnut": "yy", "pawfect": "yyy",
            "money gun": "u", "corgi": "m", "you're amazing": "m",
            "little hippo": "h", "baby hippo": "h", "rhythmic bear": "m",
            "dancing bear": "m", "motorcycle": "yyyyy",
        },
        "follow_seq": "v", "follow_once": True,
        "like_seq": "r", "like_target": 100,
    },
]

# ══════════════════════════════════════════════════════════════════
#  QSS STYLES
# ══════════════════════════════════════════════════════════════════
SETTINGS_QSS = qss("""
QDialog { background:#0f0f0f; color:#e0e0e0; font-family:'Segoe UI',sans-serif; }
QLabel { color:#b0b0b0; }
QLineEdit, QSpinBox, QComboBox {
    background:#1a1a1a; color:#f0f0f0;
    border:1px solid #2a2a2a; border-radius:6px; padding:6px 10px; font-size:13px;
}
QLineEdit:focus, QSpinBox:focus { border:1px solid #fe2c55; }
QComboBox::drop-down { border:none; width:18px; }
QComboBox QAbstractItemView { background:#1a1a1a; color:#f0f0f0; selection-background-color:#fe2c55; }
QPushButton { border-radius:7px; padding:7px 14px; font-weight:700; font-size:13px; border:none; }
QCheckBox { color:#c0c0c0; spacing:8px; }
QCheckBox::indicator { width:15px; height:15px; border-radius:4px; border:1px solid #444; background:#1a1a1a; }
QCheckBox::indicator:checked { background:#fe2c55; border:1px solid #fe2c55; }
QScrollArea { border:none; background:transparent; }
QScrollBar:vertical { background:#0a0a0a; width:5px; border-radius:3px; }
QScrollBar::handle:vertical { background:#2a2a2a; border-radius:3px; min-height:20px; }
QListWidget {
    background:#0a0a0a; border:1px solid #1e1e1e; border-radius:8px;
    color:#e0e0e0; font-size:13px; outline:none;
}
QListWidget::item { padding:9px 14px; border-bottom:1px solid #161616; }
QListWidget::item:selected { background:#1a0008; color:#fe2c55; border-left:3px solid #fe2c55; }
QListWidget::item:hover { background:#141414; }
QTabWidget::pane { border:1px solid #1e1e1e; border-radius:8px; background:#0f0f0f; }
QTabBar::tab { background:#141414; color:#666; padding:8px 18px; border-radius:6px 6px 0 0;
               margin-right:2px; font-weight:700; }
QTabBar::tab:selected { background:#0f0f0f; color:#fe2c55; border-bottom:2px solid #fe2c55; }
""")

MAIN_QSS = qss("""
QMainWindow, QWidget {
    background:#0f0f0f; color:#e0e0e0;
    font-family:'Segoe UI', sans-serif;
}
QLabel { color:#c0c0c0; }
QLineEdit, QComboBox {
    background:#1a1a1a; color:#f0f0f0;
    border:1px solid #242424; border-radius:7px;
    padding:8px 12px; font-size:13px;
}
QLineEdit:focus, QComboBox:focus { border:1px solid #fe2c55; }
QComboBox::drop-down { border:none; width:20px; }
QComboBox QAbstractItemView {
    background:#1a1a1a; color:#f0f0f0;
    selection-background-color:#fe2c55;
}
QPushButton {
    border-radius:7px; padding:8px 16px;
    font-weight:700; font-size:13px; color:white; border:none;
}
QTextEdit {
    background:#080808; color:#00ff00;
    font-family:Consolas,monospace; font-size:12px;
    border-radius:10px; padding:10px; border:1px solid #181818;
}
QScrollBar:vertical { background:#0a0a0a; width:5px; border-radius:3px; }
QScrollBar::handle:vertical { background:#222; border-radius:3px; }
""")

# ══════════════════════════════════════════════════════════════════
#  GIFT CARD WIDGET
# ══════════════════════════════════════════════════════════════════
GIFT_CARD_IMG = 72
GIFT_CARD_W   = 106
GIFT_CARD_H   = 104


class GiftCardWidget(QWidget):
    clicked = pyqtSignal(str, int)

    def __init__(self, name, price, parent=None):
        super().__init__(parent)
        self.gift_name  = name
        self.gift_price = price
        self._selected  = False
        self.setFixedSize(GIFT_CARD_W, GIFT_CARD_H)
        self.setCursor(Qt.PointingHandCursor)
        self._build_ui()

    def _build_ui(self):
        vl = QVBoxLayout(self)
        vl.setContentsMargins(4, 5, 4, 4)
        vl.setSpacing(2)
        vl.setAlignment(Qt.AlignHCenter | Qt.AlignTop)

        self.img_lbl = QLabel()
        self.img_lbl.setFixedSize(GIFT_CARD_IMG, GIFT_CARD_IMG)
        self.img_lbl.setAlignment(Qt.AlignCenter)
        _ss(self.img_lbl, "background:#111; border-radius:6px; border:1px solid #222;")
        pix = get_gift_pixmap(self.gift_name)
        if pix and not pix.isNull():
            scaled = pix.scaled(GIFT_CARD_IMG - 2, GIFT_CARD_IMG - 2,
                                Qt.KeepAspectRatio, Qt.SmoothTransformation)
            self.img_lbl.setPixmap(scaled)
        else:
            self.img_lbl.setText("🎁")
            _ss(self.img_lbl, "background:#111; border-radius:6px; border:1px solid #222;"
                                        " color:#555; font-size:16px;")
        vl.addWidget(self.img_lbl, alignment=Qt.AlignHCenter)

        name_short = self.gift_name if len(self.gift_name) <= 13 else self.gift_name[:12] + "…"
        self.name_lbl = QLabel(name_short)
        self.name_lbl.setAlignment(Qt.AlignCenter)
        self.name_lbl.setToolTip(self.gift_name)
        _ss(self.name_lbl, "color:#ccc; font-size:10px; background:transparent;")
        vl.addWidget(self.name_lbl)

        self.price_lbl = QLabel(f"💎{format_price(self.gift_price)}")
        self.price_lbl.setAlignment(Qt.AlignCenter)
        _ss(self.price_lbl, "color:#a78bfa; font-size:10px; font-weight:700; background:transparent;")
        vl.addWidget(self.price_lbl)

        self._refresh_style()

    def set_selected(self, v):
        self._selected = bool(v)
        self._refresh_style()

    def _refresh_style(self):
        if self._selected:
            _ss(self, 
                "GiftCardWidget { background:#1a0010; border:2px solid #fe2c55; border-radius:8px; }")
        else:
            _ss(self, 
                "GiftCardWidget { background:#141414; border:1px solid #222; border-radius:8px; }"
                "GiftCardWidget:hover { background:#1e1e1e; border:1px solid #fe2c55; }")

    def mousePressEvent(self, e):
        if e.button() == Qt.LeftButton:
            self.clicked.emit(self.gift_name, self.gift_price)


# ══════════════════════════════════════════════════════════════════
#  GIFT PICKER DIALOG
# ══════════════════════════════════════════════════════════════════
class GiftPickerDialog(QDialog):
    gift_selected = pyqtSignal(str, int)
    COLS = 6

    def __init__(self, current_name="", parent=None):
        super().__init__(parent)
        self.setWindowTitle(T("gift_picker_title"))
        self.setMinimumSize(760, 580)
        self.resize(800, 640)
        _ss(self, """
            QDialog { background:#0a0a0a; color:#e0e0e0; font-family:'Segoe UI',sans-serif; }
            QLineEdit { background:#141414; color:#f0f0f0; border:1px solid #2a2a2a; border-radius:8px; padding:8px 14px; font-size:13px; }
            QLineEdit:focus { border:1px solid #fe2c55; }
            QPushButton { border-radius:7px; padding:7px 14px; font-weight:700; font-size:13px; border:none; }
            QScrollArea { border:none; background:transparent; }
            QScrollBar:vertical { background:#0a0a0a; width:5px; border-radius:3px; }
            QScrollBar::handle:vertical { background:#2a2a2a; border-radius:3px; min-height:20px; }
            QLabel { color:#bbb; }
        """)
        self._current_name = (current_name or "").strip().lower()
        self._cards       = []
        self._selected_card = None
        self._all_items   = list(GIFT_CATALOG)
        self._build_ui()
        self._populate_grid(self._all_items)
        QTimer.singleShot(80, self._scroll_to_selected)

    def _build_ui(self):
        root = QVBoxLayout(self)
        root.setContentsMargins(0, 0, 0, 0)
        root.setSpacing(0)

        topbar = QFrame()
        topbar.setFixedHeight(52)
        _ss(topbar, "background:#080808; border-bottom:1px solid #181818;")
        tb = QHBoxLayout(topbar)
        tb.setContentsMargins(16, 0, 14, 0)
        tb.setSpacing(10)
        title = QLabel(T("gift_picker_title"))
        _ss(title, "color:white; font-size:15px; font-weight:900;")
        tb.addWidget(title)
        tb.addStretch(1)
        btn_x = QPushButton("✕")
        btn_x.setFixedSize(30, 30)
        _ss(btn_x, "background:#1a1a1a; color:#aaa; border:1px solid #2a2a2a; border-radius:6px; font-weight:900;")
        btn_x.clicked.connect(self.reject)
        tb.addWidget(btn_x)
        root.addWidget(topbar)

        filter_bar = QFrame()
        _ss(filter_bar, "background:#0d0d0d; border-bottom:1px solid #181818;")
        fl = QHBoxLayout(filter_bar)
        fl.setContentsMargins(14, 8, 14, 8)
        fl.setSpacing(10)
        self.search_edit = QLineEdit()
        self.search_edit.setPlaceholderText(T("gift_search_placeholder"))
        self.search_edit.textChanged.connect(self._on_search)
        fl.addWidget(self.search_edit, 1)
        self.sel_lbl = QLabel(T("gift_picker_not_sel"))
        _ss(self.sel_lbl, "color:#555; font-size:12px; min-width:200px;")
        fl.addWidget(self.sel_lbl)
        root.addWidget(filter_bar)

        self._grid_container = QWidget()
        _ss(self._grid_container, "background:transparent;")
        self._grid_layout   = QGridLayout(self._grid_container)
        self._grid_layout.setContentsMargins(10, 10, 10, 10)
        self._grid_layout.setSpacing(5)
        self._grid_layout.setAlignment(Qt.AlignTop | Qt.AlignLeft)

        self._scroll = QScrollArea()
        self._scroll.setWidget(self._grid_container)
        self._scroll.setWidgetResizable(True)
        self._scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        root.addWidget(self._scroll, 1)

        btm = QFrame()
        btm.setFixedHeight(52)
        _ss(btm, "background:#080808; border-top:1px solid #181818;")
        bl = QHBoxLayout(btm)
        bl.setContentsMargins(16, 0, 16, 0)
        bl.setSpacing(10)
        self.confirm_lbl = QLabel("")
        _ss(self.confirm_lbl, "color:#a78bfa; font-size:13px; font-weight:700;")
        bl.addWidget(self.confirm_lbl)
        bl.addStretch(1)
        btn_cancel = QPushButton(T("gift_picker_cancel"))
        _ss(btn_cancel, "background:#1a1a1a; color:#aaa; border:1px solid #2a2a2a;")
        btn_cancel.clicked.connect(self.reject)
        bl.addWidget(btn_cancel)
        self.btn_ok = QPushButton(T("gift_picker_confirm"))
        _ss(self.btn_ok, "background:#fe2c55; color:white;")
        self.btn_ok.setEnabled(False)
        self.btn_ok.clicked.connect(self._confirm)
        bl.addWidget(self.btn_ok)
        root.addWidget(btm)

    def _populate_grid(self, items):
        for card in self._cards:
            self._grid_layout.removeWidget(card)
            card.setParent(None)
        self._cards.clear()
        self._selected_card = None

        for idx, (name, price) in enumerate(items):
            card = GiftCardWidget(name, price)
            card.clicked.connect(self._on_card_clicked)
            if name.lower() == self._current_name:
                card.set_selected(True)
                self._selected_card = card
            row, col = divmod(idx, self.COLS)
            self._grid_layout.addWidget(card, row, col)
            self._cards.append(card)
        self._update_sel_label()

    def _on_search(self, text):
        text     = text.strip().lower()
        filtered = self._all_items if not text else [
            (n, p) for n, p in self._all_items if text in n.lower()]
        self._populate_grid(filtered)
        for card in self._cards:
            if card.gift_name.lower() == self._current_name:
                card.set_selected(True)
                self._selected_card = card

    def _on_card_clicked(self, name, price):
        if self._selected_card:
            self._selected_card.set_selected(False)
        for card in self._cards:
            if card.gift_name == name:
                card.set_selected(True)
                self._selected_card = card
                break
        self._current_name = name.lower()
        self.confirm_lbl.setText(f"✅  {name}  💎 {format_price(price)}")
        self.btn_ok.setEnabled(True)
        self._update_sel_label()

    def _update_sel_label(self):
        if self._current_name:
            price  = GIFT_PRICE_MAP.get(self._current_name, 0)
            proper = self._current_name
            for n, _ in GIFT_CATALOG:
                if n.lower() == self._current_name:
                    proper = n; break
            self.sel_lbl.setText(f"เลือก: {proper}  💎{format_price(price)}")
            _ss(self.sel_lbl, "color:#a78bfa; font-size:12px; font-weight:700;")
            self.confirm_lbl.setText(f"✅  {proper}  💎 {format_price(price)}")
            self.btn_ok.setEnabled(True)
        else:
            self.sel_lbl.setText(T("gift_picker_not_sel"))
            _ss(self.sel_lbl, "color:#555; font-size:12px;")

    def _scroll_to_selected(self):
        if self._selected_card:
            self._scroll.ensureWidgetVisible(self._selected_card)

    def _confirm(self):
        if not self._current_name: return
        price  = GIFT_PRICE_MAP.get(self._current_name, 0)
        proper = self._current_name
        for n, _ in GIFT_CATALOG:
            if n.lower() == self._current_name:
                proper = n; break
        self.gift_selected.emit(proper, price)
        self.accept()


# ══════════════════════════════════════════════════════════════════
#  GIFT ROW WIDGET
# ══════════════════════════════════════════════════════════════════
# ══════════════════════════════════════════════════════════════════
#  GIFT ROW WIDGET  — Gift Map row: picker + key + optional Win +/-
#  ✅ checkbox Win เปิด/ปิดฟิลด์ win_delta
#  การทำงาน: กดคีย์เสมอ / บวก-ลบ Win เฉพาะเมื่อ chk_win เปิด
# ══════════════════════════════════════════════════════════════════
class GiftRowWidget(QWidget):
    deleted = pyqtSignal(object)
    THUMB   = 28

    def __init__(self, gift_name="", key_seq="",
                 win_enabled=False, win_delta=1, parent=None):
        super().__init__(parent)
        _ss(self, "background:transparent;")
        self.setFixedHeight(46)
        self._gift_name = (gift_name or "").strip()

        hl = QHBoxLayout(self)
        hl.setContentsMargins(0, 2, 0, 2)
        hl.setSpacing(5)

        # thumbnail
        self.thumb = QLabel()
        self.thumb.setFixedSize(self.THUMB, self.THUMB)
        self.thumb.setAlignment(Qt.AlignCenter)
        _ss(self.thumb, 
            "background:#111;border-radius:5px;border:1px solid #222;color:#444;font-size:12px;")
        hl.addWidget(self.thumb)

        # picker
        self.pick_btn = QPushButton()
        self.pick_btn.setMinimumWidth(145)
        self.pick_btn.setFixedHeight(34)
        _ss(self.pick_btn, 
            "QPushButton{background:#1a1a1a;color:#f0f0f0;border:1px solid #2a2a2a;"
            "border-radius:6px;padding:0 8px;font-size:12px;text-align:left;}"
            "QPushButton:hover{border:1px solid #fe2c55;background:#1e1e1e;}")
        self.pick_btn.clicked.connect(self._open_picker)
        hl.addWidget(self.pick_btn, 1)

        # price label
        self.price_lbl = QLabel("")
        self.price_lbl.setFixedWidth(68)
        self.price_lbl.setAlignment(Qt.AlignLeft | Qt.AlignVCenter)
        _ss(self.price_lbl, "color:#a78bfa;font-size:10px;font-weight:700;")
        hl.addWidget(self.price_lbl)

        # key
        self.key_edit = QLineEdit(key_seq)
        self.key_edit.setPlaceholderText("key")
        self.key_edit.setFixedWidth(58)
        self.key_edit.setFixedHeight(34)
        _ss(self.key_edit, 
            "background:#1a1a1a;color:#f0f0f0;border:1px solid #2a2a2a;"
            "border-radius:6px;padding:4px 6px;font-size:13px;font-weight:700;")
        hl.addWidget(self.key_edit)

        # Win toggle
        self.chk_win = QCheckBox()
        self.chk_win.setChecked(bool(win_enabled))
        self.chk_win.setToolTip("+/- Win (optional)")
        self.chk_win.setFixedWidth(16)
        _ss(self.chk_win, 
            "QCheckBox::indicator{width:13px;height:13px;border-radius:3px;"
            "border:1px solid #444;background:#1a1a1a;}"
            "QCheckBox::indicator:checked{background:#22c55e;border:1px solid #22c55e;}")
        self.chk_win.toggled.connect(lambda: self.win_spin.setEnabled(self.chk_win.isChecked()))
        hl.addWidget(self.chk_win)

        lbl_w = QLabel("W:")
        _ss(lbl_w, "color:#22c55e;font-size:10px;font-weight:700;")
        lbl_w.setFixedWidth(16)
        hl.addWidget(lbl_w)

        self.win_spin = QSpinBox()
        self.win_spin.setLocale(QLocale(QLocale.C))
        self.win_spin.setRange(-99, 99)
        self.win_spin.setValue(int(win_delta) if win_delta else 1)
        self.win_spin.setFixedWidth(52)
        self.win_spin.setFixedHeight(34)
        _ss(self.win_spin, 
            "background:#1a1a1a;color:#f0f0f0;border:1px solid #2a2a2a;"
            "border-radius:5px;padding:1px 2px;font-size:12px;font-weight:700;")
        self.win_spin.setEnabled(bool(win_enabled))
        hl.addWidget(self.win_spin)

        # delete
        btn_del = QPushButton("✕")
        btn_del.setFixedSize(26, 26)
        _ss(btn_del, 
            "background:#300;color:#ff5555;border-radius:4px;font-weight:900;border:none;padding:0;")
        btn_del.clicked.connect(lambda: self.deleted.emit(self))
        hl.addWidget(btn_del)

        self._refresh_display()

    def _open_picker(self):
        dlg = GiftPickerDialog(current_name=self._gift_name, parent=self)
        dlg.gift_selected.connect(self._on_gift_selected)
        dlg.exec_()

    def _on_gift_selected(self, name, price):
        self._gift_name = name
        self._refresh_display()

    def _refresh_display(self):
        name = self._gift_name
        if name:
            pix = get_gift_pixmap(name)
            if pix and not pix.isNull():
                scaled = pix.scaled(self.THUMB-2, self.THUMB-2,
                                    Qt.KeepAspectRatio, Qt.SmoothTransformation)
                self.thumb.setPixmap(scaled); self.thumb.setText("")
            else:
                self.thumb.setPixmap(QPixmap()); self.thumb.setText("🎁")
            self.pick_btn.setText(f"  {name}")
            price = GIFT_PRICE_MAP.get(name.lower())
            self.price_lbl.setText(f"💎{format_price(price)}" if price is not None else "")
        else:
            self.thumb.setPixmap(QPixmap()); self.thumb.setText("🎁")
            self.pick_btn.setText("  " + T("gift_picker_click"))
            self.price_lbl.setText("")

    def get_data(self):
        """คืน (gift_name_lower, key_seq, win_enabled, win_delta)"""
        return (self._gift_name.strip().lower(),
                self.key_edit.text().strip(),
                self.chk_win.isChecked(),
                self.win_spin.value())


# ══════════════════════════════════════════════════════════════════
#  WIN RULE ROW WIDGET  (ใช้ใน SettingsDialog Tab Win Rules)
#  สามารถเลือก: ✅ บวก/ลบ Win  ✅ กดคีย์  หรือทั้งสองพร้อมกัน
# ══════════════════════════════════════════════════════════════════
class WinRuleRowWidget(QWidget):
    """แถวกฎ: เลือกของขวัญ → ตั้ง +/-Win และ/หรือ กดคีย์"""
    deleted = pyqtSignal(object)
    THUMB   = 26

    def __init__(self, gift_name="", win_enabled=True, win_delta=1,
                 key_enabled=False, key_seq="", parent=None):
        super().__init__(parent)
        _ss(self, "background:transparent;")
        self.setFixedHeight(48)
        self._gift_name = (gift_name or "").strip()

        hl = QHBoxLayout(self)
        hl.setContentsMargins(0, 2, 0, 2)
        hl.setSpacing(6)

        # thumbnail
        self.thumb = QLabel()
        self.thumb.setFixedSize(self.THUMB, self.THUMB)
        self.thumb.setAlignment(Qt.AlignCenter)
        _ss(self.thumb, 
            "background:#111; border-radius:4px; border:1px solid #222; color:#444; font-size:11px;")
        hl.addWidget(self.thumb)

        # ปุ่มเลือกของขวัญ
        self.pick_btn = QPushButton()
        self.pick_btn.setFixedHeight(32)
        self.pick_btn.setMinimumWidth(130)
        _ss(self.pick_btn, 
            "QPushButton{background:#1a1a1a;color:#f0f0f0;border:1px solid #2a2a2a;"
            "border-radius:5px;padding:0 7px;font-size:11px;text-align:left;}"
            "QPushButton:hover{border:1px solid #fe2c55;}")
        self.pick_btn.clicked.connect(self._open_picker)
        hl.addWidget(self.pick_btn, 2)

        # ── Win section ──────────────────────────────────────────
        self.chk_win = QCheckBox()
        self.chk_win.setChecked(win_enabled)
        self.chk_win.setFixedWidth(18)
        _ss(self.chk_win, 
            "QCheckBox::indicator{width:14px;height:14px;border-radius:3px;"
            "border:1px solid #444;background:#1a1a1a;}"
            "QCheckBox::indicator:checked{background:#22c55e;border:1px solid #22c55e;}")
        self.chk_win.toggled.connect(self._refresh_enabled)
        hl.addWidget(self.chk_win)

        lbl_w = QLabel("Win:")
        _ss(lbl_w, "color:#22c55e; font-size:11px; font-weight:700;")
        lbl_w.setFixedWidth(26)
        hl.addWidget(lbl_w)

        self.delta_spin = QSpinBox()
        self.delta_spin.setLocale(QLocale(QLocale.C))
        self.delta_spin.setRange(-99, 99)
        self.delta_spin.setValue(int(win_delta))
        self.delta_spin.setFixedWidth(62)
        self.delta_spin.setFixedHeight(32)
        _ss(self.delta_spin, 
            "background:#1a1a1a;color:#f0f0f0;border:1px solid #2a2a2a;"
            "border-radius:5px;padding:2px 4px;font-size:12px;font-weight:700;")
        hl.addWidget(self.delta_spin)

        # ── Key section ──────────────────────────────────────────
        self.chk_key = QCheckBox()
        self.chk_key.setChecked(key_enabled)
        self.chk_key.setFixedWidth(18)
        _ss(self.chk_key, 
            "QCheckBox::indicator{width:14px;height:14px;border-radius:3px;"
            "border:1px solid #444;background:#1a1a1a;}"
            "QCheckBox::indicator:checked{background:#fe2c55;border:1px solid #fe2c55;}")
        self.chk_key.toggled.connect(self._refresh_enabled)
        hl.addWidget(self.chk_key)

        lbl_k = QLabel("Key:")
        _ss(lbl_k, "color:#fe2c55; font-size:11px; font-weight:700;")
        lbl_k.setFixedWidth(26)
        hl.addWidget(lbl_k)

        self.key_edit = QLineEdit(key_seq)
        self.key_edit.setPlaceholderText("คีย์")
        self.key_edit.setFixedWidth(72)
        self.key_edit.setFixedHeight(32)
        _ss(self.key_edit, 
            "background:#1a1a1a;color:#f0f0f0;border:1px solid #2a2a2a;"
            "border-radius:5px;padding:4px 6px;font-size:12px;")
        hl.addWidget(self.key_edit)

        # ── ลบ ──────────────────────────────────────────────────
        btn_del = QPushButton("✕")
        btn_del.setFixedSize(24, 24)
        _ss(btn_del, 
            "background:#300;color:#ff5555;border-radius:4px;font-weight:900;border:none;padding:0;")
        btn_del.clicked.connect(lambda: self.deleted.emit(self))
        hl.addWidget(btn_del)

        self._refresh_display()
        self._refresh_enabled()

    def _open_picker(self):
        dlg = GiftPickerDialog(current_name=self._gift_name, parent=self)
        dlg.gift_selected.connect(self._on_gift_selected)
        dlg.exec_()

    def _on_gift_selected(self, name, price):
        self._gift_name = name
        self._refresh_display()

    def _refresh_display(self):
        if self._gift_name:
            pix = get_gift_pixmap(self._gift_name)
            if pix and not pix.isNull():
                scaled = pix.scaled(self.THUMB - 2, self.THUMB - 2,
                                    Qt.KeepAspectRatio, Qt.SmoothTransformation)
                self.thumb.setPixmap(scaled)
                self.thumb.setText("")
            else:
                self.thumb.setPixmap(QPixmap())
                self.thumb.setText("🎁")
            price = GIFT_PRICE_MAP.get(self._gift_name.lower(), 0)
            self.pick_btn.setText(
                f"  {self._gift_name}  💎{format_price(price)}" if price else f"  {self._gift_name}")
        else:
            self.thumb.setPixmap(QPixmap())
            self.thumb.setText("🎁")
            self.pick_btn.setText("  คลิกเพื่อเลือกของขวัญ…")

    def _refresh_enabled(self):
        self.delta_spin.setEnabled(self.chk_win.isChecked())
        self.key_edit.setEnabled(self.chk_key.isChecked())

    def get_data(self):
        """คืน dict กฎ"""
        return {
            "gift_name":   self._gift_name.lower(),
            "win_enabled": self.chk_win.isChecked(),
            "win_delta":   self.delta_spin.value(),
            "key_enabled": self.chk_key.isChecked(),
            "key_seq":     self.key_edit.text().strip(),
        }


# ══════════════════════════════════════════════════════════════════
#  SETTINGS DIALOG
# ══════════════════════════════════════════════════════════════════
class SettingsDialog(QDialog):
    presets_changed = pyqtSignal(list)

    def __init__(self, presets, current_idx, parent=None):
        super().__init__(parent)
        self.setWindowTitle(T("settings_title"))
        self.setMinimumSize(980, 700)
        _ss(self, SETTINGS_QSS)

        self._presets      = copy.deepcopy(presets)
        self._current_idx  = max(0, min(current_idx, len(self._presets) - 1))
        self._gift_rows    = []
        self._loading      = False

        root = QVBoxLayout(self)
        root.setContentsMargins(0, 0, 0, 0)
        root.setSpacing(0)

        topbar = QFrame()
        _ss(topbar, "background:#080808; border-bottom:1px solid #1a1a1a;")
        topbar.setFixedHeight(52)
        tb = QHBoxLayout(topbar)
        tb.setContentsMargins(18, 0, 18, 0)
        tb.setSpacing(8)

        self.lbl_title_s = QLabel(T("settings_title"))
        _ss(self.lbl_title_s, "color:white; font-size:15px; font-weight:900;")
        tb.addWidget(self.lbl_title_s)
        tb.addStretch(1)

        self.btn_imp = QPushButton(T("import_btn"))
        _ss(self.btn_imp, "background:#1d4ed8; color:white;")
        self.btn_imp.clicked.connect(self._import_presets)

        self.btn_exp = QPushButton(T("export_btn"))
        _ss(self.btn_exp, "background:#92400e; color:white;")
        self.btn_exp.clicked.connect(self._export_presets)

        self.btn_save = QPushButton(T("save_btn"))
        _ss(self.btn_save, "background:#fe2c55; color:white;")
        self.btn_save.clicked.connect(self._save_all)

        btn_close = QPushButton("✕")
        _ss(btn_close, "background:#222; color:#aaa; border:1px solid #333;")
        btn_close.setFixedWidth(34)
        btn_close.clicked.connect(self.reject)

        for b in (self.btn_imp, self.btn_exp, self.btn_save, btn_close):
            tb.addWidget(b)
        root.addWidget(topbar)

        body = QHBoxLayout()
        body.setContentsMargins(0, 0, 0, 0)
        body.setSpacing(0)

        sidebar = QFrame()
        sidebar.setFixedWidth(210)
        _ss(sidebar, "background:#080808; border-right:1px solid #141414;")
        sl = QVBoxLayout(sidebar)
        sl.setContentsMargins(10, 14, 10, 14)
        sl.setSpacing(8)

        self.lbl_presets_sidebar = self._lbl(T("presets_label"), "#555", 11)
        sl.addWidget(self.lbl_presets_sidebar)

        self.preset_list = QListWidget()
        self.preset_list.currentRowChanged.connect(self._on_preset_select)
        sl.addWidget(self.preset_list, 1)

        btn_row = QHBoxLayout()
        btn_row.setSpacing(6)
        self.btn_add_p = QPushButton(T("add_preset_btn"))
        _ss(self.btn_add_p, "background:#166534; color:white;")
        self.btn_add_p.clicked.connect(self._add_preset)
        self.btn_del_p = QPushButton(T("del_preset_btn"))
        _ss(self.btn_del_p, "background:#7f1d1d; color:white;")
        self.btn_del_p.clicked.connect(self._del_preset)
        btn_row.addWidget(self.btn_add_p)
        btn_row.addWidget(self.btn_del_p)
        sl.addLayout(btn_row)

        self.btn_dup_p = QPushButton(T("dup_preset_btn"))
        _ss(self.btn_dup_p, "background:#3b0764; color:white;")
        self.btn_dup_p.clicked.connect(self._dup_preset)
        sl.addWidget(self.btn_dup_p)

        body.addWidget(sidebar)

        content = QFrame()
        _ss(content, "background:#0f0f0f;")
        cl = QVBoxLayout(content)
        cl.setContentsMargins(22, 16, 22, 16)
        cl.setSpacing(14)

        name_row = QHBoxLayout()
        self.lbl_preset_name = QLabel(T("preset_name_label"))
        self.lbl_preset_name.setFixedWidth(100)
        _ss(self.lbl_preset_name, "color:#888; font-weight:700;")
        self.preset_name_edit = QLineEdit()
        self.preset_name_edit.setPlaceholderText(T("preset_name_placeholder"))
        self.preset_name_edit.textChanged.connect(self._sync_name)
        name_row.addWidget(self.lbl_preset_name)
        name_row.addWidget(self.preset_name_edit)
        cl.addLayout(name_row)

        sep = QFrame()
        sep.setFrameShape(QFrame.HLine)
        _ss(sep, "border:none; border-top:1px solid #1a1a1a;")
        cl.addWidget(sep)

        self.tabs = QTabWidget()
        cl.addWidget(self.tabs, 1)

        # TAB 1: Gift Map
        tab_gift = QWidget()
        _ss(tab_gift, "background:transparent;")
        tg = QVBoxLayout(tab_gift)
        tg.setContentsMargins(0, 10, 0, 0)
        tg.setSpacing(8)

        hdr_row = QHBoxLayout()
        hdr_img = self._lbl("🖼", "#555", 11)
        hdr_img.setFixedWidth(36)
        self.lbl_gift_col_name = self._lbl(T("gift_col_name"), "#555", 11)
        self.lbl_gift_col_key  = self._lbl(T("gift_col_key"), "#555", 11)
        self.lbl_gift_col_key.setFixedWidth(76)
        lbl_del_h = self._lbl("", "#555", 11)
        lbl_del_h.setFixedWidth(34)
        hdr_row.addWidget(hdr_img)
        hdr_row.addWidget(self.lbl_gift_col_name, 1)
        hdr_row.addWidget(self.lbl_gift_col_key)
        hdr_row.addWidget(lbl_del_h)
        tg.addLayout(hdr_row)

        self._gift_scroll_w  = QWidget()
        _ss(self._gift_scroll_w, "background:transparent;")
        self._gift_rows_layout = QVBoxLayout(self._gift_scroll_w)
        self._gift_rows_layout.setContentsMargins(0, 0, 4, 0)
        self._gift_rows_layout.setSpacing(5)
        self._gift_rows_layout.addStretch(1)

        gift_scroll = QScrollArea()
        gift_scroll.setWidget(self._gift_scroll_w)
        gift_scroll.setWidgetResizable(True)
        tg.addWidget(gift_scroll, 1)

        self.btn_add_gift = QPushButton(T("add_gift_btn"))
        _ss(self.btn_add_gift, "background:#166534; color:white;")
        self.btn_add_gift.setFixedWidth(180)
        self.btn_add_gift.clicked.connect(lambda: self._add_gift_row())
        tg.addWidget(self.btn_add_gift)

        self.tabs.addTab(tab_gift, T("tab_gift"))

        # TAB 2: Follow / Like
        tab_fl = QWidget()
        _ss(tab_fl, "background:transparent;")
        fl = QVBoxLayout(tab_fl)
        fl.setContentsMargins(0, 18, 0, 0)
        fl.setSpacing(18)

        # ── Follow card ───────────────────────────────────────────
        fc  = self._card()
        fcl = QVBoxLayout(fc)
        fcl.setContentsMargins(16, 14, 16, 14)
        fcl.setSpacing(12)

        # header row: ชื่อ section + toggle enable
        fhdr = QHBoxLayout()
        self.lbl_follow_sec = self._lbl(T("follow_section"), "white", 14, bold=True)
        fhdr.addWidget(self.lbl_follow_sec)
        fhdr.addStretch(1)
        self.follow_enabled_chk = QCheckBox(T("follow_enabled_chk"))
        self.follow_enabled_chk.setChecked(True)
        _ss(self.follow_enabled_chk, 
            "QCheckBox{color:#22c55e;font-weight:700;font-size:12px;}"
            "QCheckBox::indicator{width:15px;height:15px;border-radius:4px;"
            "border:1px solid #444;background:#1a1a1a;}"
            "QCheckBox::indicator:checked{background:#22c55e;border:1px solid #22c55e;}")
        self.follow_enabled_chk.toggled.connect(self._refresh_follow_enabled)
        fhdr.addWidget(self.follow_enabled_chk)
        fcl.addLayout(fhdr)

        self._follow_body = QWidget()
        _ss(self._follow_body, "background:transparent;")
        fb_lay = QVBoxLayout(self._follow_body)
        fb_lay.setContentsMargins(0,0,0,0)
        fb_lay.setSpacing(10)

        fr1 = QHBoxLayout()
        self.lbl_follow_key   = self._lbl(T("follow_key_label"), "#999", w=120)
        self.follow_seq_edit  = QLineEdit()
        self.follow_seq_edit.setPlaceholderText("r")
        self.follow_seq_edit.setMaximumWidth(130)
        fr1.addWidget(self.lbl_follow_key)
        fr1.addWidget(self.follow_seq_edit)
        fr1.addStretch(1)
        fb_lay.addLayout(fr1)

        fr2 = QHBoxLayout()
        self.follow_once_chk = QCheckBox(T("follow_once_chk"))
        _ss(self.follow_once_chk, "color:#fbbf24; font-weight:700;")
        fr2.addWidget(self.follow_once_chk)
        fr2.addStretch(1)
        fb_lay.addLayout(fr2)

        fcl.addWidget(self._follow_body)
        fl.addWidget(fc)

        # ── Like card ─────────────────────────────────────────────
        lc  = self._card()
        lcl = QVBoxLayout(lc)
        lcl.setContentsMargins(16, 14, 16, 14)
        lcl.setSpacing(12)

        # header row: ชื่อ section + toggle enable
        lhdr = QHBoxLayout()
        self.lbl_like_sec = self._lbl(T("like_section"), "white", 14, bold=True)
        lhdr.addWidget(self.lbl_like_sec)
        lhdr.addStretch(1)
        self.like_enabled_chk = QCheckBox(T("like_enabled_chk"))
        self.like_enabled_chk.setChecked(True)
        _ss(self.like_enabled_chk, 
            "QCheckBox{color:#22c55e;font-weight:700;font-size:12px;}"
            "QCheckBox::indicator{width:15px;height:15px;border-radius:4px;"
            "border:1px solid #444;background:#1a1a1a;}"
            "QCheckBox::indicator:checked{background:#22c55e;border:1px solid #22c55e;}")
        self.like_enabled_chk.toggled.connect(self._refresh_like_enabled)
        lhdr.addWidget(self.like_enabled_chk)
        lcl.addLayout(lhdr)

        self._like_body = QWidget()
        _ss(self._like_body, "background:transparent;")
        lb_lay = QVBoxLayout(self._like_body)
        lb_lay.setContentsMargins(0,0,0,0)
        lb_lay.setSpacing(10)

        lr1 = QHBoxLayout()
        self.lbl_like_key  = self._lbl(T("like_key_label"), "#999", w=120)
        self.like_seq_edit = QLineEdit()
        self.like_seq_edit.setPlaceholderText("v")
        self.like_seq_edit.setMaximumWidth(130)
        lr1.addWidget(self.lbl_like_key)
        lr1.addWidget(self.like_seq_edit)
        lr1.addStretch(1)
        lb_lay.addLayout(lr1)

        lr2 = QHBoxLayout()
        self.lbl_like_target  = self._lbl(T("like_target_label"), "#999", w=120)
        self.like_target_spin = QSpinBox()
        self.like_target_spin.setLocale(QLocale(QLocale.C))
        self.like_target_spin.setRange(1, 999999)
        self.like_target_spin.setValue(100)
        self.like_target_spin.setMaximumWidth(130)
        self.lbl_like_unit = self._lbl(T("like_target_unit"), "#555")
        lr2.addWidget(self.lbl_like_target)
        lr2.addWidget(self.like_target_spin)
        lr2.addWidget(self.lbl_like_unit)
        lr2.addStretch(1)
        lb_lay.addLayout(lr2)

        lcl.addWidget(self._like_body)
        fl.addWidget(lc)
        fl.addStretch(1)
        self.tabs.addTab(tab_fl, T("tab_follow_like"))

        # ── TAB 3: Win Rules ──────────────────────────────────────
        tab_win = QWidget()
        _ss(tab_win, "background:transparent;")
        tw = QVBoxLayout(tab_win)
        tw.setContentsMargins(0, 10, 0, 0)
        tw.setSpacing(10)

        # card: Win Counter ทั่วไป
        wc1 = self._card()
        wc1l = QVBoxLayout(wc1)
        wc1l.setContentsMargins(14, 12, 14, 12)
        wc1l.setSpacing(10)
        lbl_wc = self._lbl(T("win_counter_section"), "white", 13, bold=True)
        wc1l.addWidget(lbl_wc)

        wr1 = QHBoxLayout()
        wr1.setSpacing(10)
        wr1.addWidget(self._lbl(T("win_max_label"), "#999", w=90))
        self.win_max_spin = QSpinBox()
        self.win_max_spin.setLocale(QLocale(QLocale.C))
        self.win_max_spin.setRange(1, 9999)
        self.win_max_spin.setValue(10)
        self.win_max_spin.setMaximumWidth(100)
        wr1.addWidget(self.win_max_spin)
        wr1.addSpacing(20)
        wr1.addWidget(self._lbl(T("win_font_size"), "#999", w=70))
        self.win_font_spin = QSpinBox()
        self.win_font_spin.setLocale(QLocale(QLocale.C))
        self.win_font_spin.setRange(12, 72)
        self.win_font_spin.setValue(28)
        self.win_font_spin.setMaximumWidth(80)
        wr1.addWidget(self.win_font_spin)
        wr1.addStretch(1)
        wc1l.addLayout(wr1)

        wr2 = QHBoxLayout()
        wr2.setSpacing(10)
        wr2.addWidget(self._lbl(T("win_bg_label"), "#999", w=90))
        self.win_bg_combo = QComboBox()
        for lbl_bg, _, _ in get_overlay_bg_options():
            self.win_bg_combo.addItem(lbl_bg)
        self.win_bg_combo.setMaximumWidth(180)
        wr2.addWidget(self.win_bg_combo)
        wr2.addSpacing(10)
        wr2.addWidget(self._lbl(T("win_text_color"), "#999", w=80))
        self.win_tc_combo = QComboBox()
        for lbl_tc, col_tc in get_text_color_options():
            self.win_tc_combo.addItem(lbl_tc, col_tc)
        self.win_tc_combo.setMaximumWidth(100)
        wr2.addWidget(self.win_tc_combo)
        wr2.addStretch(1)
        wc1l.addLayout(wr2)

        wr3 = QHBoxLayout()
        wr3.setSpacing(10)
        wr3.addWidget(self._lbl(T("win_hotkey_up"), "#999", w=90))
        self.win_hk_up_edit = QLineEdit()
        self.win_hk_up_edit.setPlaceholderText("up / F1")
        self.win_hk_up_edit.setMaximumWidth(110)
        wr3.addWidget(self.win_hk_up_edit)
        wr3.addWidget(self._lbl(T("win_hotkey_dn"), "#999", w=70))
        self.win_hk_dn_edit = QLineEdit()
        self.win_hk_dn_edit.setPlaceholderText("down / F2")
        self.win_hk_dn_edit.setMaximumWidth(110)
        wr3.addWidget(self.win_hk_dn_edit)
        wr3.addWidget(self._lbl(T("win_hotkey_rst"), "#999", w=50))
        self.win_hk_rst_edit = QLineEdit()
        self.win_hk_rst_edit.setPlaceholderText("F3")
        self.win_hk_rst_edit.setMaximumWidth(90)
        wr3.addWidget(self.win_hk_rst_edit)
        wr3.addStretch(1)
        wc1l.addLayout(wr3)
        tw.addWidget(wc1)

        # card: Gift → Win Rules
        wc2 = self._card()
        wc2l = QVBoxLayout(wc2)
        wc2l.setContentsMargins(14, 12, 14, 12)
        wc2l.setSpacing(8)

        tr_row = QHBoxLayout()
        lbl_rules = self._lbl(T("gift_win_rules"), "white", 13, bold=True)
        tr_row.addWidget(lbl_rules)
        tr_row.addStretch(1)
        self.btn_add_win_rule = QPushButton(T("add_win_rule"))
        _ss(self.btn_add_win_rule, "background:#166534; color:white; padding:5px 12px;")
        self.btn_add_win_rule.clicked.connect(lambda: self._add_win_rule_row())
        tr_row.addWidget(self.btn_add_win_rule)
        wc2l.addLayout(tr_row)

        # header
        hdr_win = QHBoxLayout()
        hdr_win.setSpacing(4)
        lbl_h_img = self._lbl("🖼", "#555", 10)
        lbl_h_img.setFixedWidth(32)
        lbl_h_name = self._lbl(T("win_col_header_gift"), "#555", 10)
        lbl_h_act = self._lbl(T("win_col_header_act"), "#555", 10)
        hdr_win.addWidget(lbl_h_img)
        hdr_win.addWidget(lbl_h_name, 2)
        hdr_win.addWidget(lbl_h_act, 3)
        hdr_win.addWidget(self._lbl("", "#555", 10, w=28))
        wc2l.addLayout(hdr_win)

        sep_w = QFrame()
        sep_w.setFrameShape(QFrame.HLine)
        _ss(sep_w, "border:none; border-top:1px solid #1e1e1e;")
        wc2l.addWidget(sep_w)

        self._win_rules_w = QWidget()
        _ss(self._win_rules_w, "background:transparent;")
        self._win_rules_layout = QVBoxLayout(self._win_rules_w)
        self._win_rules_layout.setContentsMargins(0, 0, 4, 0)
        self._win_rules_layout.setSpacing(4)
        self._win_rules_layout.addStretch(1)

        win_scroll = QScrollArea()
        win_scroll.setWidget(self._win_rules_w)
        win_scroll.setWidgetResizable(True)
        win_scroll.setMinimumHeight(180)
        wc2l.addWidget(win_scroll)
        tw.addWidget(wc2, 1)

        self._win_rule_rows = []   # list ของ WinRuleRowWidget
        self.tabs.addTab(tab_win, "🏆 Win Rules")

        body.addWidget(content, 1)
        root.addLayout(body)

        sbar = QFrame()
        sbar.setFixedHeight(34)
        _ss(sbar, "background:#060606; border-top:1px solid #141414;")
        sbl = QHBoxLayout(sbar)
        sbl.setContentsMargins(18, 0, 18, 0)
        self._status_lbl = QLabel(T("status_ready_settings"))
        _ss(self._status_lbl, "color:#444; font-size:12px;")
        sbl.addWidget(self._status_lbl)
        sbl.addStretch(1)
        self._catalog_lbl = QLabel(f"🎁 Catalog: {len(GIFT_CATALOG)} gifts")
        _ss(self._catalog_lbl, "color:#333; font-size:11px;")
        sbl.addWidget(self._catalog_lbl)
        root.addWidget(sbar)

        self._rebuild_sidebar()
        if self._presets:
            self.preset_list.setCurrentRow(self._current_idx)

    @staticmethod
    def _lbl(text, color="#aaa", size=13, bold=False, w=None):
        l = QLabel(text)
        _ss(l, f"color:{color}; font-size:{size}px; {'font-weight:700;' if bold else ''}")
        if w: l.setFixedWidth(w)
        return l

    @staticmethod
    def _card():
        f = QFrame()
        _ss(f, "background:#141414; border-radius:10px; border:1px solid #1e1e1e;")
        return f

    def _rebuild_sidebar(self):
        self.preset_list.blockSignals(True)
        self.preset_list.clear()
        for i, p in enumerate(self._presets):
            self.preset_list.addItem(f"  {i+1}. {p.get('name','Preset')}")
        self.preset_list.blockSignals(False)

    def _sync_name(self, text):
        idx = self.preset_list.currentRow()
        if 0 <= idx < len(self._presets):
            self._presets[idx]["name"] = text
            item = self.preset_list.item(idx)
            if item: item.setText(f"  {idx+1}. {text}")

    def _on_preset_select(self, idx):
        if self._loading: return
        cur  = self.preset_list.currentRow()
        prev = cur if cur != idx else -1
        if prev != -1: self._flush(prev)
        self._load(idx)

    def _flush(self, idx):
        if not (0 <= idx < len(self._presets)): return
        p = self._presets[idx]
        p["name"]        = self.preset_name_edit.text().strip() or f"Preset {idx+1}"
        # gift_map: { gift_name_lower: {"key": str, "win_enabled": bool, "win_delta": int} }
        new_map = {}
        for row in self._gift_rows:
            g, k, we, wd = row.get_data()
            if g:
                new_map[g] = {"key": k, "win_enabled": we, "win_delta": wd}
        p["gift_map"]    = new_map
        p["follow_enabled"] = self.follow_enabled_chk.isChecked()
        p["follow_seq"]     = self.follow_seq_edit.text().strip() or "r"
        p["follow_once"]    = self.follow_once_chk.isChecked()
        p["like_enabled"]   = self.like_enabled_chk.isChecked()
        p["like_seq"]       = self.like_seq_edit.text().strip() or "v"
        p["like_target"]    = self.like_target_spin.value()
        # win overlay settings
        p["win_max"]       = self.win_max_spin.value()
        p["win_font_size"] = self.win_font_spin.value()
        p["win_bg_idx"]    = self.win_bg_combo.currentIndex()
        p["win_tc"]        = self.win_tc_combo.currentData() or "#ffffff"
        p["win_hk_up"]     = self.win_hk_up_edit.text().strip()
        p["win_hk_dn"]     = self.win_hk_dn_edit.text().strip()
        p["win_hk_rst"]    = self.win_hk_rst_edit.text().strip()

    def _load(self, idx):
        if not (0 <= idx < len(self._presets)): return
        self._loading = True
        p = self._presets[idx]
        self.preset_name_edit.setText(p.get("name", f"Preset {idx+1}"))
        fe = bool(p.get("follow_enabled", True))
        self.follow_enabled_chk.setChecked(fe)
        self.follow_seq_edit.setText(p.get("follow_seq", "r"))
        self.follow_once_chk.setChecked(bool(p.get("follow_once", True)))
        self._refresh_follow_enabled()
        le = bool(p.get("like_enabled", True))
        self.like_enabled_chk.setChecked(le)
        self.like_seq_edit.setText(p.get("like_seq", "v"))
        self.like_target_spin.setValue(int(p.get("like_target", 100)))
        self._refresh_like_enabled()
        self._rebuild_gift_rows(p.get("gift_map", {}))
        self.win_max_spin.setValue(int(p.get("win_max", 10)))
        self.win_font_spin.setValue(int(p.get("win_font_size", 28)))
        self.win_bg_combo.setCurrentIndex(int(p.get("win_bg_idx", 0)))
        tc_idx = self.win_tc_combo.findData(p.get("win_tc", "#ffffff"))
        if tc_idx >= 0: self.win_tc_combo.setCurrentIndex(tc_idx)
        self.win_hk_up_edit.setText(p.get("win_hk_up", "up"))
        self.win_hk_dn_edit.setText(p.get("win_hk_dn", "down"))
        self.win_hk_rst_edit.setText(p.get("win_hk_rst", ""))
        self._loading = False

    def _rebuild_gift_rows(self, gift_map):
        """รองรับ format เก่า {name: key_str} และ format ใหม่ {name: {key,win_enabled,win_delta}}"""
        for row in list(self._gift_rows):
            row.setParent(None); row.deleteLater()
        self._gift_rows.clear()
        for g, v in (gift_map or {}).items():
            if isinstance(v, dict):
                self._add_gift_row(g, v.get("key",""), v.get("win_enabled",False), v.get("win_delta",1))
            else:
                self._add_gift_row(g, str(v) if v else "")

    def _add_gift_row(self, gift_name="", key_seq="", win_enabled=False, win_delta=1):
        row = GiftRowWidget(gift_name, key_seq, win_enabled=win_enabled, win_delta=win_delta)
        row.deleted.connect(self._remove_gift_row)
        self._gift_rows.append(row)
        count = self._gift_rows_layout.count()
        self._gift_rows_layout.insertWidget(count - 1, row)

    def _remove_gift_row(self, row):
        if row in self._gift_rows: self._gift_rows.remove(row)
        row.setParent(None); row.deleteLater()

    def _refresh_follow_enabled(self):
        enabled = self.follow_enabled_chk.isChecked()
        self._follow_body.setEnabled(enabled)
        _ss(self._follow_body, 
            "background:transparent;" if enabled else
            "background:transparent; opacity:0.4;")
        # visual dim
        for w in (self.follow_seq_edit, self.follow_once_chk, self.lbl_follow_key):
            w.setEnabled(enabled)

    def _refresh_like_enabled(self):
        enabled = self.like_enabled_chk.isChecked()
        self._like_body.setEnabled(enabled)
        for w in (self.like_seq_edit, self.like_target_spin,
                  self.lbl_like_key, self.lbl_like_target, self.lbl_like_unit):
            w.setEnabled(enabled)

    # ── Win Rule helpers ─────────────────────────────────────────
    def _add_win_rule_row(self, gift_name="", win_enabled=True, win_delta=1,
                          key_enabled=False, key_seq=""):
        row = WinRuleRowWidget(
            gift_name=gift_name, win_enabled=win_enabled, win_delta=win_delta,
            key_enabled=key_enabled, key_seq=key_seq)
        row.deleted.connect(self._remove_win_rule_row)
        self._win_rule_rows.append(row)
        count = self._win_rules_layout.count()
        self._win_rules_layout.insertWidget(count - 1, row)

    def _remove_win_rule_row(self, row):
        if row in self._win_rule_rows: self._win_rule_rows.remove(row)
        row.setParent(None); row.deleteLater()

    def _add_preset(self):
        cur = self.preset_list.currentRow()
        self._flush(cur)
        new_p = {"name": f"Preset {len(self._presets)+1}",
                 "gift_map": {}, "follow_seq": "r", "follow_once": True,
                 "like_seq": "v", "like_target": 100}
        self._presets.append(new_p)
        self._rebuild_sidebar()
        new_idx = len(self._presets) - 1
        self.preset_list.setCurrentRow(new_idx)
        self._load(new_idx)

    def _del_preset(self):
        idx = self.preset_list.currentRow()
        if len(self._presets) <= 1:
            QMessageBox.warning(self, T("cant_del_title"), T("cant_del_msg")); return
        if not (0 <= idx < len(self._presets)): return
        name = self._presets[idx].get("name", f"Preset {idx+1}")
        if QMessageBox.question(self, T("confirm_del_title"),
                                T("confirm_del_msg", name=name),
                                QMessageBox.Yes | QMessageBox.No) != QMessageBox.Yes:
            return
        self._presets.pop(idx)
        self._rebuild_sidebar()
        new_idx = max(0, idx - 1)
        self.preset_list.setCurrentRow(new_idx)
        self._load(new_idx)

    def _dup_preset(self):
        idx = self.preset_list.currentRow()
        self._flush(idx)
        if not (0 <= idx < len(self._presets)): return
        new_p = copy.deepcopy(self._presets[idx])
        new_p["name"] = new_p.get("name", "Preset") + " (copy)"
        self._presets.append(new_p)
        self._rebuild_sidebar()
        new_idx = len(self._presets) - 1
        self.preset_list.setCurrentRow(new_idx)
        self._load(new_idx)

    def _import_presets(self):
        path, _ = QFileDialog.getOpenFileName(self, T("import_title"), "",
                                               "JSON Files (*.json);;All Files (*)")
        if not path: return
        try:
            with open(path, "r", encoding="utf-8") as f:
                data = json.load(f)
            if isinstance(data, dict) and "presets" in data:
                loaded = data["presets"]
            elif isinstance(data, list):
                loaded = data
            else:
                raise ValueError(T("import_bad_format"))
            validated = []
            for i, p in enumerate(loaded):
                if not isinstance(p, dict): continue
                validated.append({
                    "name":        str(p.get("name", f"Imported {i+1}")),
                    "gift_map":    {str(k): str(v) for k, v in (p.get("gift_map") or {}).items()},
                    "follow_seq":  str(p.get("follow_seq", "r")),
                    "follow_once": bool(p.get("follow_once", True)),
                    "like_seq":    str(p.get("like_seq", "v")),
                    "like_target": int(p.get("like_target", 100)),
                })
            if not validated: raise ValueError(T("import_empty"))
            r = QMessageBox.question(self, "Import",
                                     T("import_confirm", n=len(validated)),
                                     QMessageBox.Yes | QMessageBox.No | QMessageBox.Cancel)
            if r == QMessageBox.Cancel: return
            cur = self.preset_list.currentRow()
            self._flush(cur)
            if r == QMessageBox.Yes:
                self._presets = validated
            else:
                self._presets.extend(validated)
            self._rebuild_sidebar()
            self.preset_list.setCurrentRow(0)
            self._load(0)
            self._set_status(f"📥 Imported {len(validated)} preset(s)")
        except Exception as e:
            QMessageBox.critical(self, T("import_fail_title"), str(e))

    def _export_presets(self):
        cur = self.preset_list.currentRow()
        self._flush(cur)
        path, _ = QFileDialog.getSaveFileName(self, T("export_title"), "presets.json",
                                               "JSON Files (*.json);;All Files (*)")
        if not path: return
        try:
            data = {"presets": self._presets, "exported_at": time.strftime("%Y-%m-%d %H:%M:%S")}
            with open(path, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
            self._set_status(f"📤 Exported {len(self._presets)} preset(s)")
            QMessageBox.information(self, T("export_ok_title"), T("export_ok_msg", path=path))
        except Exception as e:
            QMessageBox.critical(self, T("export_fail_title"), str(e))

    def _save_all(self):
        cur = self.preset_list.currentRow()
        self._flush(cur)
        self.presets_changed.emit(copy.deepcopy(self._presets))
        self._set_status("💾 Saved")
        self.accept()

    def get_win_settings_from_preset(self, p: dict) -> dict:
        """แยก win settings ออกจาก preset dict"""
        bi = int(p.get("win_bg_idx", 0))
        opts = get_overlay_bg_options()
        _, bg_col, is_ch = opts[bi] if 0 <= bi < len(opts) else opts[0]
        return {
            "hotkey_up":          p.get("win_hk_up",    "up"),
            "hotkey_dn":          p.get("win_hk_dn",    "down"),
            "hotkey_reset":       p.get("win_hk_rst",   ""),
            "max_win":            int(p.get("win_max",   10)),
            "overlay_bg":         bg_col,
            "overlay_chroma":     is_ch,
            "overlay_text_color": p.get("win_tc",        "#ffffff"),
            "overlay_font_size":  int(p.get("win_font_size", 28)),
            "gift_win_rules":     p.get("win_rules",     []),
        }

    def _set_status(self, msg):
        self._status_lbl.setText(msg)

    def get_presets(self):
        return copy.deepcopy(self._presets)


# ══════════════════════════════════════════════════════════════════
#  AVATAR CACHE & WIDGET
# ══════════════════════════════════════════════════════════════════
class AvatarCache:
    def __init__(self):
        self._cache = {}
        self._lock  = threading.Lock()

    def get(self, url):
        url = (url or "").strip()
        if not url: return None
        with self._lock: return self._cache.get(url)

    def set(self, url, pix):
        url = (url or "").strip()
        if not url or pix is None: return
        with self._lock: self._cache[url] = pix


def _fetch_image_bytes(url):
    url = (url or "").strip()
    if not url: return None
    if url.startswith("//"): url = "https:" + url
    if not (url.startswith("http://") or url.startswith("https://")): return None
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/122.0.0.0 Safari/537.36",
        "Referer":    "https://www.tiktok.com/",
        "Accept":     "image/avif,image/webp,image/apng,image/*,*/*;q=0.8",
    }
    try:
        req = urllib.request.Request(url, headers=headers)
        with urllib.request.urlopen(req, timeout=8) as resp:
            return resp.read()
    except Exception:
        try:
            h2  = {k: v for k, v in headers.items() if k != "Referer"}
            req2 = urllib.request.Request(url, headers=h2)
            with urllib.request.urlopen(req2, timeout=8) as resp:
                return resp.read()
        except Exception:
            return None


class AvatarWidget(QWidget):
    def __init__(self, size=48, border_color="#ffffff", border_w=3, parent=None):
        super().__init__(parent)
        self._pix         = None
        self._size        = int(size)
        self._border_color = border_color
        self._border_w    = int(border_w)
        self.setFixedSize(self._size, self._size)

    def set_border(self, color, width=3):
        self._border_color = color
        self._border_w     = int(width)
        self.update()

    def set_pixmap(self, pix):
        # skip repaint ถ้าเป็น QPixmap เดิม (ป้องกัน flicker)
        if pix is self._pix:
            return
        self._pix = pix
        self.update()

    @staticmethod
    def _center_crop_square(pix):
        if pix is None or pix.isNull(): return QPixmap()
        w, h = pix.width(), pix.height()
        side  = min(w, h)
        x     = (w - side) // 2
        y     = (h - side) // 2
        return pix.copy(x, y, side, side)

    def paintEvent(self, event):
        p  = QPainter(self)
        p.setRenderHint(QPainter.Antialiasing, True)
        r  = self.rect()
        bw = max(2, self._border_w)
        outer = r.adjusted(bw // 2, bw // 2, -bw // 2, -bw // 2)
        inner = r.adjusted(bw, bw, -bw, -bw)
        if self._pix is not None and not self._pix.isNull():
            sq     = self._center_crop_square(self._pix)
            scaled = sq.scaled(inner.width(), inner.height(),
                                Qt.IgnoreAspectRatio, Qt.SmoothTransformation)
            path = QPainterPath()
            path.addEllipse(QRectF(inner))
            p.setClipPath(path)
            p.drawPixmap(inner.left(), inner.top(), scaled)
            p.setClipping(False)
        else:
            p.setPen(Qt.NoPen)
            p.setBrush(QColor(255, 255, 255, 220))
            p.drawEllipse(QRectF(inner))
        pen = QPen(QColor(self._border_color))
        pen.setWidth(bw)
        p.setPen(pen)
        p.setBrush(Qt.NoBrush)
        p.drawEllipse(QRectF(outer))
        p.end()


# ══════════════════════════════════════════════════════════════════
#  TOP BOARD (Green Screen overlay)
# ══════════════════════════════════════════════════════════════════
# ══════════════════════════════════════════════════════════════════
#  TOP BOARD  — transparent overlay, zero-flicker
#  หลักการ: สร้าง widget ครั้งเดียว → refresh text/avatar in-place
#  paintEvent เคลียร์ด้วย CompositionMode_Clear (เหมือน GiftJarOverlay)
# ══════════════════════════════════════════════════════════════════
# ══════════════════════════════════════════════════════════════════
#  TOP BOARD OVERLAY  — โปร่งใส 100%, ไม่มีกรอบ, ไม่มีปุ่มกากบาท
#  ลาก: กดซ้ายค้าง   ซ่อน: ดับเบิ้ลคลิก
# ══════════════════════════════════════════════════════════════════
# ══════════════════════════════════════════════════════════════════
#  TOP BOARD OVERLAY  — โปร่งใส 100%, ไม่มีกรอบ, ไม่มีปุ่มปิด
#  ทุก slot มีขนาดเท่ากัน — align ตรงทุกอันดับ
#  ลาก: กดซ้ายค้าง   ซ่อน: ดับเบิ้ลคลิก
# ══════════════════════════════════════════════════════════════════
# ══════════════════════════════════════════════════════════════════
#  TOP BOARD OVERLAY  — โปร่งใส 100%, ไม่มีกรอบ, ไม่มีปุ่มปิด
#  5 slot เรียงบนลงล่าง ขนาดเท่ากัน align ตรงทุกอันดับ
#  มี title บนสุด  |  ลาก: กดซ้ายค้าง  |  ซ่อน: ดับเบิ้ลคลิก
# ══════════════════════════════════════════════════════════════════
# ══════════════════════════════════════════════════════════════════
#  TOP BOARD OVERLAY
#  ใช้ Chroma Key (#00b140) เป็น background แบบเดียวกับ GiftJarOverlay
#  → OBS: ใส่ Filter "Color Key" ค่า #00b140 จะโปร่งใสทันที
#  ลาก: กดซ้ายค้าง  |  ซ่อน: ดับเบิ้ลคลิก
# ══════════════════════════════════════════════════════════════════
# ══════════════════════════════════════════════════════════════════
#  TOP BOARD (Green Screen overlay)
#  พื้นหลังสีเขียว #00ff00 — OBS ใช้ Color Key filter กรองออก
#  วาดด้วย QPainter เพื่อให้ OBS Window Capture จับได้บน Windows
# ══════════════════════════════════════════════════════════════════
class TopBoardGreenWindow(QDialog):
    def __init__(self, title_text, mode, parent=None):
        super().__init__(parent)
        self.setWindowTitle(title_text)
        self.setFixedSize(360, 560)
        self.setWindowFlags(Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.Tool)
        # ใช้ solid green — OBS จับได้ 100% ไม่มี fringe
        self.setAttribute(Qt.WA_TranslucentBackground, False)
        self.setStyleSheet("background: #00ff00;")
        self.title_text = title_text
        self.mode = (mode or "likers").lower().strip()
        self._drag_pos = None

        root = QVBoxLayout(self)
        root.setContentsMargins(16, 14, 16, 14)
        root.setSpacing(10)

        # Title
        icon = "❤️" if self.mode == "likers" else "🪙"
        self.title_lbl = QLabel(f"{icon}  {title_text}")
        self.title_lbl.setStyleSheet(
            "color: white; font-weight: 900; font-size: 18px; background: transparent;")
        root.addWidget(self.title_lbl)

        self.list_layout = QVBoxLayout()
        self.list_layout.setSpacing(14)
        root.addLayout(self.list_layout)
        root.addStretch(1)

    def paintEvent(self, event):
        """วาด green background ด้วย QPainter — OBS Window Capture จับได้บน Windows"""
        p = QPainter(self)
        p.fillRect(self.rect(), QColor(0, 255, 0))
        p.end()

    def _clear(self):
        while self.list_layout.count():
            it = self.list_layout.takeAt(0)
            w = it.widget()
            if w:
                w.deleteLater()

    @staticmethod
    def _rank_badge_style(i):
        if i == 0: return ("#f5b400", "#000000", "#d49800")
        if i == 1: return ("#d7d7d7", "#000000", "#bdbdbd")
        if i == 2: return ("#ff8a00", "#000000", "#e07a00")
        return ("#ffffff", "#000000", "#d0d0d0")

    def _make_rank_column(self, i):
        col = QFrame()
        col.setStyleSheet("background: transparent;")
        v = QVBoxLayout(col)
        v.setContentsMargins(0, 0, 0, 0)
        v.setSpacing(3)
        crown = QLabel("👑" if i == 0 else "")
        crown.setAlignment(Qt.AlignCenter)
        crown.setStyleSheet("font-size: 15px; background: transparent;")
        crown.setFixedHeight(18)
        v.addWidget(crown)
        bg, fg, bd = self._rank_badge_style(i)
        badge = QLabel(str(i + 1))
        badge.setAlignment(Qt.AlignCenter)
        badge.setFixedSize(30, 30)
        badge.setStyleSheet(
            f"background:{bg}; color:{fg}; font-weight:900; font-size:14px;"
            f"border-radius:15px; border:2px solid {bd};")
        v.addWidget(badge, alignment=Qt.AlignCenter)
        col.setFixedWidth(34)
        return col

    def update_list(self, items, avatar_provider):
        self._clear()
        for i, item in enumerate((items or [])[:5]):
            name = (item.get("name") or item.get("displayName") or "Unknown").strip()
            if self.mode == "gifters":
                value = int(item.get("coins") or 0)
                icon  = "🪙"
                value_color = "#ffffff"
            else:
                value = int(item.get("score") or 0)
                icon  = "❤"
                value_color = "#ff3333"

            pix = avatar_provider(item) if callable(avatar_provider) else None

            row = QFrame()
            row.setStyleSheet("background: transparent;")
            hl = QHBoxLayout(row)
            hl.setContentsMargins(0, 0, 0, 0)
            hl.setSpacing(10)
            hl.addWidget(self._make_rank_column(i))

            size = 60 if i == 0 else 50
            ring = "#ffb300" if i == 0 else "#ffa000"
            bw   = 4 if i == 0 else 3
            av = AvatarWidget(size=size, border_color=ring, border_w=bw)
            av.set_pixmap(pix if isinstance(pix, QPixmap) else None)
            hl.addWidget(av)

            text_wrap = QFrame()
            text_wrap.setStyleSheet("background: transparent;")
            tl = QVBoxLayout(text_wrap)
            tl.setContentsMargins(0, 0, 0, 0)
            tl.setSpacing(5)

            name_fs = 18 if i == 0 else 15
            lbl_name = QLabel(name)
            lbl_name.setStyleSheet(
                f"color: #2aa8ff; font-weight: 900; font-size: {name_fs}px;"
                "background: transparent;")
            lbl_name.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Preferred)
            lbl_name.setToolTip(name)
            tl.addWidget(lbl_name)

            val_fs = 15 if i == 0 else 13
            lbl_val = QLabel(f"{icon}  {value:,}")
            lbl_val.setStyleSheet(
                f"color:{value_color}; font-weight:900; font-size:{val_fs}px;"
                "background: transparent;")
            tl.addWidget(lbl_val)
            hl.addWidget(text_wrap, 1)
            self.list_layout.addWidget(row)

    # ── drag ──────────────────────────────────────────────────────
    def mousePressEvent(self, e):
        if e.button() == Qt.LeftButton:
            self._drag_pos = e.globalPos() - self.frameGeometry().topLeft()

    def mouseMoveEvent(self, e):
        if e.buttons() == Qt.LeftButton and self._drag_pos is not None:
            self.move(e.globalPos() - self._drag_pos)

    def mouseReleaseEvent(self, e):
        self._drag_pos = None


# ══════════════════════════════════════════════════════════════════
#  SIGNALS
# ══════════════════════════════════════════════════════════════════
class Communicate(QObject):
    log_signal      = pyqtSignal(str, str)
    status_signal   = pyqtSignal(str, str)
    top_like_signal = pyqtSignal(list)
    top_gift_signal = pyqtSignal(list)
    # 🆕 ส่งชื่อของขวัญ + จำนวนให้ jar
    jar_gift_signal = pyqtSignal(str, int)


# ══════════════════════════════════════════════════════════════════
#  TIKTOK WORKER
# ══════════════════════════════════════════════════════════════════
class TikTokWorker:
    def __init__(self, comm: Communicate, press_q: queue.Queue):
        self.comm      = comm
        self.press_q   = press_q
        self.client    = None
        self.thread    = None
        self.stop_flag = threading.Event()
        self.gift_last = {}
        self.gift_map  = {}
        self.follow_enabled  = True
        self.follow_seq      = "r"
        self.follow_once     = True
        self._followed_users = set()
        self.like_enabled    = True
        self.like_seq        = "v"
        self.like_target     = 100
        self.like_counter  = 0
        self.tts_q         = None
        self.voice_mode    = VOICE_MODE_THAI_SOFT
        self.chat_rate_limit_sec = CHAT_RATE_LIMIT_SEC
        self.chat_max_chars      = CHAT_MAX_CHARS
        self._last_chat_ts       = 0.0
        self._connected_unique_id = None
        self.loop              = None
        self.like_scores       = {}
        self.gift_coins        = {}
        self._last_emit_ts     = 0.0
        self._win_overlay_ref  = None

    def _emit_top_lists(self, force=False):
        now = time.time()
        if (not force) and (now - self._last_emit_ts) < 0.25: return
        self._last_emit_ts = now
        like_list = sorted(self.like_scores.values(),
                           key=lambda x: int(x.get("score", 0)), reverse=True)[:5]
        gift_list = sorted(self.gift_coins.values(),
                           key=lambda x: int(x.get("coins", 0)), reverse=True)[:5]
        try:
            self.comm.top_like_signal.emit(like_list)
            self.comm.top_gift_signal.emit(gift_list)
        except Exception: pass

    def set_runtime(self, gift_map,
                    follow_enabled, follow_seq, follow_once,
                    like_enabled, like_seq, like_target):
        self.gift_map       = gift_map or {}
        self.follow_enabled = bool(follow_enabled)
        self.follow_seq     = (follow_seq or "r").strip()
        self.follow_once    = bool(follow_once)
        self.like_enabled   = bool(like_enabled)
        self.like_seq       = (like_seq or "v").strip()
        self.like_target    = int(like_target or 100)
        self.like_counter   = 0
        self.gift_last.clear()
        self._followed_users.clear()

    def set_voice_mode(self, mode):
        self.voice_mode = mode or VOICE_MODE_OFF

    def is_running(self):
        return self.thread is not None and self.thread.is_alive() and not self.stop_flag.is_set()

    def start(self, username_no_at):
        if self.is_running(): return
        self.stop_flag.clear()
        self._connected_unique_id = username_no_at.strip().replace("@", "")
        self.thread = threading.Thread(target=self._run_client_blocking, daemon=True)
        self.thread.start()

    def stop(self):
        self.stop_flag.set()
        try:
            if self.client is None: return
            if self.loop and self.loop.is_running():
                async def _shutdown():
                    for fn_name in ("disconnect", "close"):
                        try:
                            fn = getattr(self.client, fn_name, None)
                            if callable(fn): await fn()
                        except Exception: pass
                try:
                    asyncio.run_coroutine_threadsafe(_shutdown(), self.loop)
                except Exception: pass
        except Exception: pass

    @staticmethod
    def _safe_uid_from_comment(event):
        try:
            ui = getattr(event, "user_info", None)
            if ui is None: return "unknown"
            if hasattr(ui, "to_pydict"):
                d = ui.to_pydict()
                return d.get("uniqueId") or d.get("unique_id") or d.get("userId") or "unknown"
            return getattr(ui, "unique_id", None) or getattr(ui, "uniqueId", None) or "unknown"
        except Exception: return "unknown"

    def _run_client_blocking(self):
        u = self._connected_unique_id
        if not u:
            self.comm.log_signal.emit("❌ Missing username", "#ff3333")
            self.comm.status_signal.emit("🔴 Error", "error")
            return

        self.loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self.loop)
        try:
            self.client = TikTokLiveClient(unique_id=f"@{u}")

            @self.client.on(ConnectEvent)
            async def on_connect(event: ConnectEvent):
                self.comm.log_signal.emit(
                    f"STATUS: Connected (@{getattr(event,'unique_id',u)})", "#aaaaaa")
                self.comm.status_signal.emit("🟢 Connected!", "success")
                self._emit_top_lists(force=True)

            @self.client.on(DisconnectEvent)
            async def on_disconnect(event: DisconnectEvent):
                self.comm.log_signal.emit("STATUS: Disconnected", "#aaaaaa")
                self.comm.status_signal.emit("🔴 Disconnected", "error")

            @self.client.on(LiveEndEvent)
            async def on_live_end(event: LiveEndEvent):
                self.comm.log_signal.emit("STATUS: Stream ended", "#aaaaaa")
                self.comm.status_signal.emit("🔴 Stream ended", "error")

            @self.client.on(FollowEvent)
            async def on_follow(event: FollowEvent):
                if self.stop_flag.is_set(): return
                uid = str(getattr(getattr(event, "user", None), "unique_id", None) or "unknown").strip()
                if not self.follow_enabled:
                    self.comm.log_signal.emit(f"👤 {uid} followed (Follow Action ปิดอยู่)", "#3a3a3a")
                    return
                if self.follow_once:
                    if uid in self._followed_users:
                        self.comm.log_signal.emit(f"👤 {uid} followed (duplicate – skipped)", "#3a3a3a")
                        return
                    self._followed_users.add(uid)
                self.comm.log_signal.emit(f"👤 {uid} followed → press '{self.follow_seq}' x1", "#ffd700")
                if self.follow_seq:
                    self.press_q.put((self.follow_seq, 1, "follow"))

            @self.client.on(LikeEvent)
            async def on_like(event: LikeEvent):
                if self.stop_flag.is_set(): return
                raw_inc = (getattr(event, "like_count", None)
                           or getattr(event, "likeCount", None)
                           or getattr(event, "count", None) or 0)
                try: inc = int(raw_inc)
                except Exception: inc = 0
                if inc <= 0: return

                user_obj = getattr(event, "user", None) or getattr(event, "user_info", None)
                uid  = str(getattr(getattr(event, "user", None), "unique_id", None)
                           or getattr(user_obj, "unique_id", None)
                           or getattr(user_obj, "uniqueId", None) or "unknown").strip() or "unknown"
                name     = _pick_display_name(user_obj)
                pic_raw  = _pick_avatar(user_obj)
                urls     = extract_all_http_urls(pic_raw)
                pic      = urls[0] if urls else ""

                row  = self.like_scores.get(uid) or {"uniqueId": uid, "name": name, "pic": pic, "score": 0}
                row["name"]  = name or row.get("name", "Unknown")
                row["pic"]   = pic  or row.get("pic",  "")
                row["score"] = int(row.get("score", 0)) + inc
                self.like_scores[uid] = row
                self._emit_top_lists()

                if self.like_enabled:
                    self.like_counter += inc
                    while self.like_counter >= self.like_target:
                        self.like_counter -= self.like_target
                        self.comm.log_signal.emit(
                            f"❤️ {self.like_target} likes → press '{self.like_seq}' x1", "#ff3333")
                        if self.like_seq:
                            self.press_q.put((self.like_seq, 1, "like"))

            @self.client.on(GiftEvent)
            async def on_gift(event: GiftEvent):
                if self.stop_flag.is_set(): return
                user_obj  = getattr(event, "user", None) or getattr(event, "user_info", None)
                uid       = str(getattr(getattr(event, "user", None), "unique_id", None)
                               or getattr(user_obj, "unique_id", None)
                               or getattr(user_obj, "uniqueId", None) or "unknown").strip() or "unknown"
                gift_name = getattr(getattr(event, "gift", None), "name", None) or "UnknownGift"
                g         = norm_name(gift_name)

                try:
                    streakable = bool(getattr(event.gift, "streakable", False))
                    streaking  = bool(getattr(event, "streaking", False))
                except Exception:
                    streakable = False; streaking = False
                try: repeat_count = int(getattr(event, "repeat_count", 1) or 1)
                except Exception: repeat_count = 1

                if streakable:
                    key = f"{uid}|{g}"
                    if not streaking:
                        self.gift_last.pop(key, None); return
                    last  = int(self.gift_last.get(key, 0) or 0)
                    delta = repeat_count - last
                    if delta <= 0: return
                    self.gift_last[key] = repeat_count
                    count = delta
                else:
                    count = max(1, repeat_count)

                # ── ดึงข้อมูล user ──────────────────────────────────
                name    = _pick_display_name(user_obj)
                pic_raw = _pick_avatar(user_obj)
                urls    = extract_all_http_urls(pic_raw)
                pic     = urls[0] if urls else ""

                # ── คำนวณ coins (นับทุกของขวัญ ทั้ง mapped และ unmapped) ──
                per = 0
                for attr in ("diamond_count", "diamondCount", "value"):
                    try:
                        per = int(getattr(getattr(event, "gift", None), attr, 0) or 0)
                        if per > 0: break
                    except Exception: pass
                # fallback: ถ้า API ไม่ส่ง diamond ให้ใช้ราคาจาก catalog
                if per <= 0:
                    per = GIFT_PRICE_MAP.get(g, 1)
                coins_add = int(per) * int(count)

                # ── อัปเดต gift_coins leaderboard ───────────────────
                grow = self.gift_coins.get(uid) or {
                    "uniqueId": uid, "name": name, "pic": pic, "coins": 0}
                grow["name"]  = name or grow.get("name", "Unknown")
                grow["pic"]   = pic  or grow.get("pic",  "")
                grow["coins"] = int(grow.get("coins", 0)) + coins_add
                self.gift_coins[uid] = grow
                self._emit_top_lists()

                # ── ส่งของขวัญตกลงโหล (ทุกของขวัญ) ─────────────────
                self.comm.jar_gift_signal.emit(gift_name, count)

                # ── gift_map lookup (key press + win) ────────────────
                raw_entry   = self.gift_map.get(g)
                win_enabled = False
                win_delta   = 1
                seq         = ""

                if raw_entry is None:
                    self.comm.log_signal.emit(
                        f"⚠️ {gift_name} x{count} (unmapped, coins+{coins_add})", "#ffaa00")
                elif isinstance(raw_entry, dict):
                    seq         = raw_entry.get("key", "")
                    win_enabled = bool(raw_entry.get("win_enabled", False))
                    win_delta   = int(raw_entry.get("win_delta", 1))
                else:
                    seq = str(raw_entry)

                if seq:
                    self.comm.log_signal.emit(
                        f"🎁 {gift_name} x{count} 💎{coins_add} → key '{seq}'", "#00ff00")
                    self.press_q.put((seq, count, "gift"))
                elif raw_entry is not None:
                    self.comm.log_signal.emit(
                        f"🎁 {gift_name} x{count} 💎{coins_add} (no key)", "#aaaaaa")

                # ── Win counter ───────────────────────────────────────
                if win_enabled and self._win_overlay_ref is not None:
                    try:
                        delta_total = win_delta * count
                        if delta_total != 0:
                            _win_bridge.win_changed.emit(delta_total)
                    except Exception: pass
                if self._win_overlay_ref is not None:
                    try: self._win_overlay_ref.apply_gift_win(g, count)
                    except Exception: pass

            @self.client.on(CommentEvent)
            async def on_comment(event: CommentEvent):
                if self.stop_flag.is_set(): return
                msg = str(getattr(event, "comment", None) or "").strip()
                if not msg: return
                uid = self._safe_uid_from_comment(event)
                self.comm.log_signal.emit(f"💬 {uid}: {msg}", "#25f4ee")
                if self.voice_mode == VOICE_MODE_OFF or self.tts_q is None: return
                if not (HAS_EDGE_TTS or HAS_TTS): return
                now = time.time()
                if now - self._last_chat_ts < self.chat_rate_limit_sec: return
                self._last_chat_ts = now
                speak_text = msg[: self.chat_max_chars]
                if CHAT_FILTER_ENABLED and CHAT_FILTER_MODE == "skip":
                    speak_text, bad = sanitize_chat_for_tts(speak_text)
                    if not speak_text:
                        self.comm.log_signal.emit("🛡️ Filtered: skipped TTS", "#aaaaaa")
                        return
                self.tts_q.put(speak_text)

            self.comm.status_signal.emit(f"🟡 Connecting {u}...", "loading")
            self.client.run()

        except Exception as e:
            self.comm.log_signal.emit(f"❌ Start error: {e}", "#ff3333")
            self.comm.status_signal.emit("🔴 Connection failed", "error")
        finally:
            self.client = None
            try:
                if self.loop and not self.loop.is_closed():
                    self.loop.stop(); self.loop.close()
            except Exception: pass
            self.loop = None


# ══════════════════════════════════════════════════════════════════
#  WIN COUNTER BRIDGE
# ══════════════════════════════════════════════════════════════════
class WinSignalBridge(QObject):
    win_changed = pyqtSignal(int)

_win_bridge = WinSignalBridge()


# ══════════════════════════════════════════════════════════════════
#  GIFT WIN RULE ROW
# ══════════════════════════════════════════════════════════════════
class GiftWinRuleRow(QWidget):
    deleted = pyqtSignal(object)
    THUMB   = 28

    def __init__(self, gift_name="", win_delta=1, key_seq="", parent=None):
        super().__init__(parent)
        _ss(self, "background:transparent;")
        self.setFixedHeight(44)
        self._gift_name = (gift_name or "").strip()

        hl = QHBoxLayout(self)
        hl.setContentsMargins(0, 2, 0, 2)
        hl.setSpacing(6)

        self.thumb = QLabel()
        self.thumb.setFixedSize(self.THUMB, self.THUMB)
        self.thumb.setAlignment(Qt.AlignCenter)
        _ss(self.thumb, 
            "background:#111; border-radius:4px; border:1px solid #222; color:#444; font-size:12px;")
        hl.addWidget(self.thumb)

        self.pick_btn = QPushButton()
        self.pick_btn.setFixedHeight(34)
        _ss(self.pick_btn, 
            "QPushButton{background:#1a1a1a;color:#f0f0f0;border:1px solid #2a2a2a;"
            "border-radius:6px;padding:0 8px;font-size:12px;text-align:left;}"
            "QPushButton:hover{border:1px solid #fe2c55;}")
        self.pick_btn.clicked.connect(self._open_picker)
        hl.addWidget(self.pick_btn, 1)

        lbl_win = QLabel("Win:")
        _ss(lbl_win, "color:#888;font-size:11px;")
        lbl_win.setFixedWidth(28)
        hl.addWidget(lbl_win)

        self.delta_spin = QSpinBox()
        self.delta_spin.setLocale(QLocale(QLocale.C))
        self.delta_spin.setRange(-99, 99)
        self.delta_spin.setValue(int(win_delta))
        self.delta_spin.setFixedWidth(68)
        self.delta_spin.setFixedHeight(34)
        _ss(self.delta_spin, 
            "background:#1a1a1a;color:#f0f0f0;border:1px solid #2a2a2a;"
            "border-radius:6px;padding:2px 4px;font-size:13px;font-weight:700;")
        hl.addWidget(self.delta_spin)

        lbl_key = QLabel("Key:")
        _ss(lbl_key, "color:#888;font-size:11px;")
        lbl_key.setFixedWidth(28)
        hl.addWidget(lbl_key)

        self.key_edit = QLineEdit(key_seq)
        self.key_edit.setPlaceholderText("optional")
        self.key_edit.setFixedWidth(100)
        self.key_edit.setFixedHeight(34)
        _ss(self.key_edit, 
            "background:#1a1a1a;color:#f0f0f0;border:1px solid #2a2a2a;"
            "border-radius:6px;padding:4px 6px;font-size:12px;")
        hl.addWidget(self.key_edit)

        btn_del = QPushButton("✕")
        btn_del.setFixedSize(26, 26)
        _ss(btn_del, 
            "background:#300;color:#ff5555;border-radius:5px;font-weight:900;border:none;padding:0;")
        btn_del.clicked.connect(lambda: self.deleted.emit(self))
        hl.addWidget(btn_del)

        self._refresh_display()

    def _open_picker(self):
        dlg = GiftPickerDialog(current_name=self._gift_name, parent=self)
        dlg.gift_selected.connect(self._on_gift_selected)
        dlg.exec_()

    def _on_gift_selected(self, name, price):
        self._gift_name = name
        self._refresh_display()

    def _refresh_display(self):
        if self._gift_name:
            pix = get_gift_pixmap(self._gift_name)
            if pix and not pix.isNull():
                scaled = pix.scaled(self.THUMB - 2, self.THUMB - 2,
                                    Qt.KeepAspectRatio, Qt.SmoothTransformation)
                self.thumb.setPixmap(scaled)
                self.thumb.setText("")
            else:
                self.thumb.setPixmap(QPixmap())
                self.thumb.setText("🎁")
            price = GIFT_PRICE_MAP.get(self._gift_name.lower(), 0)
            self.pick_btn.setText(f"  {self._gift_name}  💎{format_price(price)}")
        else:
            self.thumb.setPixmap(QPixmap())
            self.thumb.setText("🎁")
            self.pick_btn.setText("  " + T("gift_picker_click"))

    def get_data(self):
        return self._gift_name.lower(), self.delta_spin.value(), self.key_edit.text().strip()


# ══════════════════════════════════════════════════════════════════
#  OVERLAY BG / TEXT COLOR OPTIONS
# ══════════════════════════════════════════════════════════════════
def get_overlay_bg_options():
    return [
        (T("bg_black"),   "#0a0a0a", False),
        (T("bg_navy"),    "#0a121a", False),
        (T("bg_dark"),    "#141414", False),
        (T("bg_darkred"), "#1a0000", False),
        (T("bg_chroma"),  "#00ff00", True),
    ]

def get_text_color_options():
    return [
        (T("tc_white"),  "#ffffff"),
        (T("tc_yellow"), "#ffd700"),
        (T("tc_red"),    "#fe2c55"),
        (T("tc_cyan"),   "#25f4ee"),
        (T("tc_green"),  "#22c55e"),
    ]


# ══════════════════════════════════════════════════════════════════
#  WIN SETTINGS DIALOG
# ══════════════════════════════════════════════════════════════════

# ══════════════════════════════════════════════════════════════════
#  WIN CONTROL TOOLBAR  (ลดขนาด — ไม่มีปุ่ม Settings แล้ว)
# ══════════════════════════════════════════════════════════════════
class WinControlToolbar(QWidget):
    def __init__(self, overlay, parent=None):
        super().__init__(parent)
        self.overlay = overlay
        self.setWindowFlags(Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.Tool)
        self.setAttribute(Qt.WA_TranslucentBackground, False)
        self.setStyleSheet(
            "QWidget{background:#0d0d0d;border:1px solid #2a2a2a;border-radius:10px;}"
            "QPushButton{border:none;border-radius:6px;font-weight:900;}")
        self._drag_pos = None
        hl = QHBoxLayout(self); hl.setContentsMargins(8,6,8,6); hl.setSpacing(6)

        def _btn(text, bg, fn):
            b = QPushButton(text); b.setFixedSize(32, 32)
            b.setStyleSheet(
                f"QPushButton{{background:{bg};color:white;font-size:15px;border-radius:7px;}}")
            b.clicked.connect(fn); return b

        hl.addWidget(_btn("+",  "#166534", lambda: self.overlay.change_win(1)))
        hl.addWidget(_btn("−",  "#7f1d1d", lambda: self.overlay.change_win(-1)))
        hl.addWidget(_btn("↺",  "#1a2030", self.overlay.reset_win))
        self.adjustSize()

    def mousePressEvent(self, e):
        if e.button() == Qt.LeftButton:
            self._drag_pos = e.globalPos() - self.frameGeometry().topLeft()
    def mouseMoveEvent(self, e):
        if e.buttons() == Qt.LeftButton and self._drag_pos is not None:
            self.move(e.globalPos() - self._drag_pos)
    def mouseReleaseEvent(self, e):
        self._drag_pos = None


class WinCounterOverlay(QWidget):
    DEFAULT_SETTINGS = {
        "hotkey_up":          "up",
        "hotkey_dn":          "down",
        "hotkey_reset":       "",
        "max_win":            10,
        "overlay_bg":         "#0a121a",
        "overlay_chroma":     False,
        "overlay_text_color": "#ffffff",
        "overlay_font_size":  28,
        "gift_win_rules":     [],
    }

    def __init__(self, parent_main=None):
        super().__init__(None)
        self.parent_main  = parent_main
        self.current_wins = 0
        self._settings    = copy.deepcopy(self.DEFAULT_SETTINGS)
        self._hk_registered = []
        self._drag_pos    = None

        self.setWindowFlags(Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.Tool)
        self.setAttribute(Qt.WA_TranslucentBackground, False)

        self._build_ui()
        self._toolbar = WinControlToolbar(overlay=self)
        self._toolbar.hide()
        _win_bridge.win_changed.connect(self._on_win_changed)
        self._apply_appearance()
        self._register_hotkeys()

    def _build_ui(self):
        hl = QHBoxLayout(self)
        hl.setContentsMargins(12, 6, 12, 6)
        hl.setSpacing(8)
        self._lbl_icon  = QLabel("🏆")
        self._lbl_win   = QLabel(T("win_label"))
        self._lbl_count = QLabel("0")
        self._lbl_slash = QLabel(" / ")
        self._lbl_max   = QLabel("10")
        for w in (self._lbl_icon, self._lbl_win,
                  self._lbl_count, self._lbl_slash, self._lbl_max):
            w.setStyleSheet("background:transparent;")
            hl.addWidget(w)
        self._lbl_count.setAlignment(Qt.AlignRight | Qt.AlignVCenter)
        self._lbl_max.setAlignment(Qt.AlignLeft  | Qt.AlignVCenter)

    def _apply_appearance(self):
        s  = self._settings
        bg = s.get("overlay_bg", "#0a121a")
        tc = s.get("overlay_text_color", "#ffffff")
        fs = int(s.get("overlay_font_size", 28))
        is_chroma = s.get("overlay_chroma", False)
        actual_bg = "#00ff00" if is_chroma else bg
        self.setStyleSheet(f"QWidget {{ background:{actual_bg}; border:none; }}")
        n  = self.current_wins
        mx = int(s.get("max_win", 10))
        num_col = "#ff3e6c" if n < 0 else ("#22c55e" if n >= mx else tc)
        base_style = f"background:transparent; font-size:{fs}px;"
        self._lbl_icon.setStyleSheet(base_style + f" color:{tc};")
        self._lbl_win.setStyleSheet( base_style + f" color:{tc}; font-weight:900;")
        self._lbl_count.setStyleSheet(base_style + f" color:{num_col}; font-weight:900;")
        self._lbl_slash.setStyleSheet(base_style + f" color:{tc};")
        self._lbl_max.setStyleSheet(  base_style + f" color:{tc}; font-weight:700;")
        self._lbl_max.setText(str(mx))
        self.adjustSize()

    def _refresh_count(self):
        s  = self._settings
        tc = s.get("overlay_text_color", "#ffffff")
        fs = int(s.get("overlay_font_size", 28))
        n  = self.current_wins
        mx = int(s.get("max_win", 10))
        self._lbl_count.setText(str(n))
        self._lbl_max.setText(str(mx))
        num_col = "#ff3e6c" if n < 0 else ("#22c55e" if n >= mx else tc)
        self._lbl_count.setStyleSheet(
            f"background:transparent; font-size:{fs}px; color:{num_col}; font-weight:900;")

    def mousePressEvent(self, e):
        if e.button() == Qt.LeftButton:
            self._drag_pos = e.globalPos() - self.frameGeometry().topLeft()
        elif e.button() == Qt.RightButton:
            self._toggle_toolbar()

    def mouseMoveEvent(self, e):
        if e.buttons() == Qt.LeftButton and self._drag_pos is not None:
            self.move(e.globalPos() - self._drag_pos)
            if self._toolbar.isVisible():
                self._toolbar.move(self.x(), self.y() + self.height() + 4)

    def mouseReleaseEvent(self, e):
        self._drag_pos = None

    def _toggle_toolbar(self):
        if self._toolbar.isVisible():
            self._toolbar.hide()
        else:
            self._toolbar.move(self.x(), self.y() + self.height() + 4)
            self._toolbar.show(); self._toolbar.raise_()

    def change_win(self, delta):
        self.current_wins += delta
        self._refresh_count()

    def reset_win(self):
        self.current_wins = 0
        self._refresh_count()

    def _on_win_changed(self, delta):
        self.current_wins += delta
        self._refresh_count()

    def apply_settings_from_preset(self, win_cfg: dict):
        """รับ win settings จาก MainWindow หลัง preset ถูก save"""
        self._settings = win_cfg
        self._apply_appearance()
        self._refresh_count()
        self._register_hotkeys()

    def _register_hotkeys(self):
        try:
            import keyboard as kb
            for hk in list(self._hk_registered):
                try: kb.remove_hotkey(hk)
                except Exception: pass
        except ImportError: pass
        self._hk_registered.clear()

        hk_up  = self._settings.get("hotkey_up",  "").strip()
        hk_dn  = self._settings.get("hotkey_dn",  "").strip()
        hk_rst = self._settings.get("hotkey_reset","").strip()
        if not (hk_up or hk_dn or hk_rst): return

        try:
            import keyboard as kb
        except ImportError: return

        import queue as _q
        if not hasattr(self, "_hk_queue"):
            self._hk_queue = _q.Queue()
            self._hk_timer = QTimer()
            self._hk_timer.setInterval(30)
            self._hk_timer.timeout.connect(self._poll_hk_queue)
            self._hk_timer.start()

        def _push(delta): self._hk_queue.put(delta)

        try:
            if hk_up:  kb.add_hotkey(hk_up,  lambda: _push(1),  suppress=False); self._hk_registered.append(hk_up)
            if hk_dn:  kb.add_hotkey(hk_dn,  lambda: _push(-1), suppress=False); self._hk_registered.append(hk_dn)
            if hk_rst: kb.add_hotkey(hk_rst, lambda: _push(0),  suppress=False); self._hk_registered.append(hk_rst)
        except Exception: pass

    def _poll_hk_queue(self):
        try:
            import queue as _q
            while True:
                delta = self._hk_queue.get_nowait()
                if delta == 0: self.reset_win()
                else: self.change_win(delta)
        except Exception: pass

    def apply_gift_win(self, gift_name_lower, count=1):
        for rule in self._settings.get("gift_win_rules", []):
            if rule.get("gift_name", "").lower() == gift_name_lower:
                # +/- win
                if rule.get("win_enabled", True):
                    delta = int(rule.get("win_delta", 0)) * int(count)
                    if delta != 0:
                        _win_bridge.win_changed.emit(delta)
                # กดคีย์
                if rule.get("key_enabled", False):
                    key = rule.get("key_seq", "").strip()
                    if key and self.parent_main and hasattr(self.parent_main, "press_q"):
                        self.parent_main.press_q.put((key, count, "win_gift"))
                break

    def closeEvent(self, e):
        self._toolbar.close(); e.accept()


# ══════════════════════════════════════════════════════════════════
#  MAIN WINDOW
# ══════════════════════════════════════════════════════════════════
class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self._state = _read_json(STATE_FILE, {})

        self.avatar_cache    = AvatarCache()
        self._avatar_pending = set()

        self._presets           = load_presets()
        self._active_preset_idx = 0

        self.init_ui()

        self.comm = Communicate()
        self.comm.log_signal.connect(self.add_log)
        self.comm.status_signal.connect(self.update_status_ui)
        self.comm.top_like_signal.connect(self._on_top_like_update)
        self.comm.top_gift_signal.connect(self._on_top_gift_update)
        # 🆕 เชื่อม jar signal
        self.comm.jar_gift_signal.connect(self._on_jar_gift)

        self.press_q   = queue.Queue()
        self.stop_worker = threading.Event()
        self.worker_thread = threading.Thread(target=self.press_worker, daemon=True)
        self.worker_thread.start()

        self.tts_q   = queue.Queue()
        self.stop_tts = threading.Event()
        self.tts_thread = None
        if HAS_EDGE_TTS or HAS_TTS:
            self.tts_thread = threading.Thread(target=self.tts_worker, daemon=True)
            self.tts_thread.start()

        self.tt = TikTokWorker(self.comm, self.press_q)
        self.tt.tts_q = self.tts_q

        self.win_likers  = TopBoardGreenWindow("TOP 5 LIKERS",  mode="likers",  parent=self)
        self.win_gifters = TopBoardGreenWindow("TOP 5 GIFTERS", mode="gifters", parent=self)

        # Win Counter Overlay
        self.win_overlay = WinCounterOverlay(parent_main=self)
        self.win_overlay.move(100, 100)

        # 🆕 Gift Jar Overlay
        self.gift_jar = GiftJarOverlay()
        # ไม่แสดงตอนเริ่ม — ผู้ใช้กดปุ่ม 🫙 เปิดเอง

        self._ui_timer = QTimer(self)
        self._ui_timer.setInterval(1000)  # 1 sec — in-place update ไม่กระพริบ
        self._ui_timer.timeout.connect(self._tick_refresh_if_visible)
        self._ui_timer.start()

        self.voice_mode = VOICE_MODE_THAI_SOFT
        self.tt.set_voice_mode(self.voice_mode)
        self.tt._win_overlay_ref = self.win_overlay

        self._restore_state()

    # ── 🆕 รับ gift จาก TikTok → ส่งลงโหล ───────────────────────
    def _on_jar_gift(self, gift_name: str, count: int):
        """เรียกจาก main thread ผ่าน Qt signal — ปลอดภัย 100%"""
        if self.gift_jar.isVisible():
            self.gift_jar.drop_gifts(gift_name, count)

    # ── avatar ───────────────────────────────────────────────────
    def avatar_provider(self, item):
        url = (item.get("pic") or "").strip()
        if not url: return None
        pix = self.avatar_cache.get(url)
        if pix is not None and not pix.isNull(): return pix
        if url not in self._avatar_pending:
            self._avatar_pending.add(url)
            threading.Thread(target=self._download_avatar, args=(url,), daemon=True).start()
        return None

    def _download_avatar(self, url):
        data = _fetch_image_bytes(url)
        if not data: return
        pix = QPixmap()
        if pix.loadFromData(data):
            self.avatar_cache.set(url, pix)

    def _tick_refresh_if_visible(self):
        """อัปเดต top board เฉพาะเมื่อมองเห็น — ใช้ in-place update ใน slot"""
        if self.win_likers.isVisible():
            like_list = sorted(self.tt.like_scores.values(),
                               key=lambda x: int(x.get("score", 0)), reverse=True)[:5]
            self.win_likers.update_list(like_list, self.avatar_provider)
        if self.win_gifters.isVisible():
            gift_list = sorted(self.tt.gift_coins.values(),
                               key=lambda x: int(x.get("coins", 0)), reverse=True)[:5]
            self.win_gifters.update_list(gift_list, self.avatar_provider)

    # ── UI (same frame, header and size as the PTVgame Launcher) ─────
    def init_ui(self):
        self.setWindowTitle(T("main_title"))
        self.setWindowFlags(Qt.Window | Qt.FramelessWindowHint)
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.resize(1160, 760)
        self.setMinimumSize(920, 660)
        self._drag_pos = None

        outer = QWidget()
        outer.setObjectName("Outer")
        outer.setStyleSheet("#Outer { background:transparent; }")
        self.setCentralWidget(outer)
        ol = QVBoxLayout(outer)
        ol.setContentsMargins(0, 0, 0, 0)

        self.frame = QFrame()
        self.frame.setObjectName("Frame")
        ol.addWidget(self.frame)
        self._frame_extra = re.sub(r"QMainWindow, QWidget \{[^}]*\}",
                                   "QWidget { color:%s; font-family:'Segoe UI',sans-serif; }" % P["text"],
                                   qss(MAIN_QSS))
        self._apply_frame_style()
        root = QVBoxLayout(self.frame)
        root.setContentsMargins(1, 1, 1, 1)
        root.setSpacing(0)

        # ── header (72 px, like the Launcher) ──
        self.header = QFrame()
        self.header.setObjectName("Header")
        self.header.setFixedHeight(72)
        self.header.setStyleSheet(
            f"#Header {{ background:{P['bg0']}; border:none; border-bottom:1px solid {P['header_line']};"
            f" border-top-left-radius:14px; border-top-right-radius:14px; }}"
            " #Header QLabel { background:transparent; }")
        self.header.installEventFilter(self)
        hl = QHBoxLayout(self.header)
        hl.setContentsMargins(22, 0, 12, 0)
        hl.setSpacing(10)

        self.lbl_main_title = QLabel(
            f'<span style="color:{P["accent"]};">◈</span> PTV<span style="color:{P["accent"]};">game</span>')
        self.lbl_main_title.setStyleSheet(f"font-size:29px; font-weight:800; color:{P['text']};")
        self.lbl_main_title.setAttribute(Qt.WA_TransparentForMouseEvents)
        hl.addWidget(self.lbl_main_title)

        sep = QFrame()
        sep.setFixedSize(1, 34)
        sep.setStyleSheet(f"background:{P['line2']}; border:none;")
        hl.addSpacing(8)
        hl.addWidget(sep)
        hl.addSpacing(8)

        tbox = QVBoxLayout()
        tbox.setSpacing(0)
        tbox.setContentsMargins(0, 0, 0, 0)
        lbl_app = QLabel("Game To Earn")
        lbl_app.setStyleSheet(f"font-size:17px; font-weight:800; color:{P['text']};")
        lbl_app.setAttribute(Qt.WA_TransparentForMouseEvents)
        self.lbl_sub = QLabel(T("header_sub"))
        self.lbl_sub.setStyleSheet(f"font-size:12px; color:{P['muted']};")
        self.lbl_sub.setAttribute(Qt.WA_TransparentForMouseEvents)
        tbox.addStretch(1)
        tbox.addWidget(lbl_app)
        tbox.addWidget(self.lbl_sub)
        tbox.addStretch(1)
        hl.addLayout(tbox)
        hl.addStretch(1)

        self.status_badge = QLabel(T("status_ready"))
        _ss(self.status_badge,
            f"background:{P['panel']}; color:{P['muted']}; border:1px solid {P['line']};"
            " border-radius:15px; padding:6px 14px; font-weight:700;")
        self.status_badge.setFixedHeight(32)
        hl.addWidget(self.status_badge, 0, Qt.AlignVCenter)
        hl.addSpacing(6)

        def win_btn(text, tip=None):
            b = QPushButton(text)
            b.setCursor(Qt.PointingHandCursor)
            b.setFixedSize(40, 36)
            b.setStyleSheet(
                f"QPushButton {{ background:transparent; color:{P['text']}; border:none; border-radius:8px;"
                f" font-size:14px; font-weight:600; padding:0; }}"
                f" QPushButton:hover {{ background:{P['panel2']}; }}")
            if tip: b.setToolTip(tip)
            return b

        self.btn_lang = QPushButton(_lang_button_label())
        self.btn_lang.setCursor(Qt.PointingHandCursor)
        self.btn_lang.setStyleSheet(
            f"QPushButton {{ background:{P['panel']}; color:{P['soft']}; border:1px solid {P['line']};"
            f" border-radius:8px; padding:6px 12px; font-size:12px; font-weight:700; }}"
            f" QPushButton:hover {{ border:1px solid {P['accent']}; }}")
        self.btn_lang.clicked.connect(self._toggle_lang)
        hl.addWidget(self.btn_lang)
        b_min = win_btn("—"); b_min.clicked.connect(self.showMinimized)
        self.btn_max = win_btn("□"); self.btn_max.clicked.connect(self._toggle_max)
        b_close = win_btn("✕"); b_close.clicked.connect(self.close)
        for b in (b_min, self.btn_max, b_close):
            hl.addWidget(b)
        root.addWidget(self.header)

        # ── body: control column (left) + live log (right) ──
        body = QHBoxLayout()
        body.setContentsMargins(0, 0, 0, 0)
        body.setSpacing(0)
        root.addLayout(body, 1)

        side_scroll = QScrollArea()
        side_scroll.setWidgetResizable(True)
        side_scroll.setFixedWidth(440)
        side_scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        side_scroll.setStyleSheet(
            f"QScrollArea {{ background:{P['bg']}; border:none; border-bottom-left-radius:14px; }}"
            f" QScrollBar:vertical {{ background:transparent; width:6px; }}"
            f" QScrollBar::handle:vertical {{ background:{P['line']}; border-radius:3px; min-height:30px; }}"
            " QScrollBar::add-line, QScrollBar::sub-line { height:0; }")
        side = QWidget()
        side.setObjectName("Side")
        side.setStyleSheet(f"#Side {{ background:{P['bg']}; }}")
        side_scroll.setWidget(side)
        layout = QVBoxLayout(side)
        layout.setContentsMargins(18, 18, 14, 18)
        layout.setSpacing(12)
        body.addWidget(side_scroll)

        def card(title_attr, title):
            f = QFrame()
            f.setObjectName("Card")
            f.setStyleSheet(
                f"#Card {{ background:{P['panel']}; border:1px solid {P['line']}; border-radius:12px; }}"
                " #Card QLabel { background:transparent; border:none; }")
            v = QVBoxLayout(f)
            v.setContentsMargins(16, 13, 16, 14)
            v.setSpacing(9)
            lbl = QLabel(title)
            lbl.setStyleSheet(f"color:{P['text']}; font-size:14px; font-weight:800;")
            setattr(self, title_attr, lbl)
            v.addWidget(lbl)
            layout.addWidget(f)
            return v

        field_css = (f"background:{P['bg']}; color:{P['text']}; padding:8px 10px; border-radius:8px;"
                     f" border:1px solid {P['line']};")

        # connect card
        cv = card("lbl_sec_conn", T("sec_connect"))
        conn = QFrame()
        conn.setObjectName("Conn")
        conn.setStyleSheet(f"#Conn {{ background:{P['bg']}; border:1px solid {P['line']}; border-radius:10px; }}")
        conn_l = QHBoxLayout(conn)
        conn_l.setContentsMargins(12, 5, 6, 5)
        conn_l.setSpacing(6)
        at = QLabel("@")
        at.setStyleSheet(f"color:{P['muted']}; font-size:15px; font-weight:800; background:transparent; border:none;")
        conn_l.addWidget(at)
        self.user_in = QLineEdit()
        self.user_in.setPlaceholderText(T("username_placeholder"))
        _ss(self.user_in, f"border:none; padding:8px 2px; color:{P['text']}; background:transparent; font-size:14px;")
        self.user_in.returnPressed.connect(lambda: (not self.tt.is_running()) and self.toggle_system())
        self.btn = QPushButton(T("connect_btn"))
        self.btn.setCursor(Qt.PointingHandCursor)
        _ss(self.btn, "background:#fe2c55; color:white; border-radius:8px; padding:10px 22px; font-weight:900; font-size:14px;")
        self.btn.clicked.connect(self.toggle_system)
        conn_l.addWidget(self.user_in, 1)
        conn_l.addWidget(self.btn)
        cv.addWidget(conn)

        # preset + voice
        pv = card("lbl_preset", T("preset_label"))
        prow = QHBoxLayout()
        prow.setSpacing(8)
        self.preset_box = QComboBox()
        self.preset_box.setStyleSheet(qss(MAIN_QSS) + "QComboBox {" + field_css + "}")
        self._rebuild_preset_combo()
        self.preset_box.currentIndexChanged.connect(self.apply_preset)
        self.btn_settings = QPushButton(T("settings_btn"))
        self.btn_settings.setCursor(Qt.PointingHandCursor)
        _ss(self.btn_settings,
            "background:#1a1a1a; color:#fbbf24; border:1px solid #333; border-radius:8px; padding:8px 14px; font-weight:700;")
        self.btn_settings.clicked.connect(self.open_settings)
        prow.addWidget(self.preset_box, 1)
        prow.addWidget(self.btn_settings)
        pv.addLayout(prow)
        self.preset_hint = QLabel("")
        self.preset_hint.setWordWrap(True)
        self.preset_hint.setStyleSheet(f"color:{P['muted']}; font-size:12px;")
        pv.addWidget(self.preset_hint)

        vrow = QHBoxLayout()
        vrow.setSpacing(8)
        self.lbl_voice = QLabel(T("voice_label"))
        self.lbl_voice.setStyleSheet(f"color:{P['muted']}; font-weight:700;")
        self.voice_box = QComboBox()
        self.voice_box.addItem(T("voice_off"),    VOICE_MODE_OFF)
        self.voice_box.addItem(T("voice_thai_m"), VOICE_MODE_THAI)
        self.voice_box.addItem(T("voice_thai_f"), VOICE_MODE_THAI_SOFT)
        self.voice_box.setCurrentIndex(2)
        self.voice_box.setStyleSheet(qss(MAIN_QSS) + "QComboBox {" + field_css + "}")
        self.voice_box.currentIndexChanged.connect(self.on_voice_change)
        vrow.addWidget(self.lbl_voice)
        vrow.addWidget(self.voice_box, 1)
        pv.addLayout(vrow)

        # overlays
        ov = card("lbl_sec_over", T("sec_overlays"))
        grid = QGridLayout()
        grid.setSpacing(8)
        self.btn_top_like = QPushButton(T("top_likers_btn"))
        _ss(self.btn_top_like, "background:#25f4ee; color:#000; border-radius:8px; padding:10px; font-weight:900; font-size:12px;")
        self.btn_top_like.clicked.connect(self.open_top_likers)
        self.btn_top_gift = QPushButton(T("top_gifters_btn"))
        _ss(self.btn_top_gift, "background:#ffd700; color:#000; border-radius:8px; padding:10px; font-weight:900; font-size:12px;")
        self.btn_top_gift.clicked.connect(self.open_top_gifters)
        self.btn_win_overlay = QPushButton(T("win_counter_btn"))
        _ss(self.btn_win_overlay,
            "background:#1a1a1a; color:#ffd700; border:1px solid #3a3000; border-radius:8px; padding:10px; font-weight:900; font-size:12px;")
        self.btn_win_overlay.clicked.connect(self.toggle_win_overlay)
        self.btn_jar = QPushButton(T("jar_btn"))
        self._jar_btn_style(False)
        self.btn_jar.clicked.connect(self.toggle_jar_overlay)
        for i, b in enumerate((self.btn_top_like, self.btn_top_gift, self.btn_win_overlay, self.btn_jar)):
            b.setCursor(Qt.PointingHandCursor)
            grid.addWidget(b, i // 2, i % 2)
        ov.addLayout(grid)

        # gift jar controls
        jv = card("lbl_sec_jar", T("sec_jar"))
        jrow = QHBoxLayout()
        jrow.setSpacing(8)
        lbl_jar_scale = QLabel("🫙 Scale")
        lbl_jar_scale.setStyleSheet(f"color:{P['muted']}; font-size:12px; font-weight:700;")
        jrow.addWidget(lbl_jar_scale)
        self.jar_slider = QSlider(Qt.Horizontal)
        self.jar_slider.setLocale(QLocale(QLocale.English, QLocale.UnitedStates))
        self.jar_slider.setRange(40, 200)
        self.jar_slider.setValue(load_jar_settings().get("scale", 100))
        self.jar_slider.setStyleSheet(
            f"QSlider {{ background:transparent; }}"
            f" QSlider::groove:horizontal{{height:4px;background:{P['line']};border-radius:2px;}}"
            f" QSlider::handle:horizontal{{background:{P['accent']};width:14px;height:14px;margin:-5px 0;border-radius:7px;}}"
            f" QSlider::sub-page:horizontal{{background:{P['accent']};border-radius:2px;}}")
        self.jar_slider.valueChanged.connect(self._on_jar_scale)
        jrow.addWidget(self.jar_slider, 1)
        self.lbl_jar_pct = QLabel(f"{self.jar_slider.value()}%")
        self.lbl_jar_pct.setStyleSheet("color:#ffd700; font-size:12px; font-weight:700;")
        self.lbl_jar_pct.setFixedWidth(42)
        jrow.addWidget(self.lbl_jar_pct)
        jv.addLayout(jrow)
        jrow2 = QHBoxLayout()
        jrow2.setSpacing(8)
        btn_jar_test = QPushButton("🎁 Test Drop")
        _ss(btn_jar_test, f"background:{P['accent_bg']}; color:{P['text']}; border:1px solid {P['accent']}; border-radius:8px; padding:7px 12px; font-size:12px; font-weight:700;")
        btn_jar_test.clicked.connect(self._jar_test_drop)
        btn_jar_clear = QPushButton("🗑 Clear")
        _ss(btn_jar_clear, "background:#4a0000; color:#ff8888; border-radius:8px; padding:7px 12px; font-size:12px; font-weight:700; border:none;")
        btn_jar_clear.clicked.connect(self._jar_clear)
        for b in (btn_jar_test, btn_jar_clear):
            b.setCursor(Qt.PointingHandCursor)
            jrow2.addWidget(b)
        jrow2.addStretch(1)
        self.lbl_jar_count = QLabel("0 gifts")
        self.lbl_jar_count.setStyleSheet(f"color:{P['muted']}; font-size:12px;")
        jrow2.addWidget(self.lbl_jar_count)
        jv.addLayout(jrow2)
        layout.addStretch(1)

        # ── right: live log on the launcher gradient ──
        main = QFrame()
        main.setObjectName("Main")
        main.setStyleSheet(
            f"#Main {{ border-top-left-radius:15px; border-bottom-right-radius:14px;"
            f" border-top:1px solid #153C5B; border-left:1px solid #153C5B;"
            f" background:qlineargradient(x1:0,y1:0,x2:0.35,y2:1, stop:0 {P['grad_a']}, stop:1 {P['grad_b']}); }}"
            " #Main QLabel { background:transparent; }")
        mv = QVBoxLayout(main)
        mv.setContentsMargins(25, 18, 24, 14)
        mv.setSpacing(10)
        lrow = QHBoxLayout()
        self.lbl_logs = QLabel(T("logs_label"))
        self.lbl_logs.setStyleSheet(f"color:{P['text']}; font-size:20px; font-weight:800;")
        lrow.addWidget(self.lbl_logs)
        lrow.addStretch(1)
        self.btn_clear_logs = QPushButton(T("clear_logs"))
        self.btn_clear_logs.setCursor(Qt.PointingHandCursor)
        self.btn_clear_logs.setStyleSheet(
            f"QPushButton {{ background:transparent; color:{P['muted']}; border:1px solid {P['line']}; border-radius:8px; padding:6px 14px; font-weight:700; }}"
            f" QPushButton:hover {{ color:{P['text']}; border:1px solid {P['accent']}; }}")
        lrow.addWidget(self.btn_clear_logs)
        mv.addLayout(lrow)

        self.logs = QTextEdit()
        self.logs.setReadOnly(True)
        self.logs.setStyleSheet(
            f"QTextEdit {{ background:{P['console']}; color:#00ff00; font-family:Consolas,monospace; font-size:12px;"
            f" border-radius:12px; padding:12px; border:1px solid #1D4366; }}"
            " QScrollBar:vertical { background:transparent; width:6px; }"
            " QScrollBar::handle:vertical { background:#27435C; border-radius:3px; min-height:30px; }"
            " QScrollBar::add-line, QScrollBar::sub-line { height:0; }")
        self.btn_clear_logs.clicked.connect(self.logs.clear)
        mv.addWidget(self.logs, 1)

        brow = QHBoxLayout()
        self.lbl_tip = QLabel(T("tip"))
        self.lbl_tip.setWordWrap(True)
        self.lbl_tip.setStyleSheet(f"color:{P['muted']}; font-size:12px;")
        brow.addWidget(self.lbl_tip, 1)
        grip = QSizeGrip(main)
        grip.setStyleSheet("background:transparent;")
        brow.addWidget(grip, 0, Qt.AlignBottom | Qt.AlignRight)
        mv.addLayout(brow)
        body.addWidget(main, 1)

        # timer อัปเดต jar count
        self._jar_count_timer = QTimer(self)
        self._jar_count_timer.setInterval(400)
        self._jar_count_timer.timeout.connect(self._update_jar_count)
        self._jar_count_timer.start()

    def _apply_frame_style(self):
        r = 0 if self.isMaximized() else 15
        self.frame.setStyleSheet(
            f"#Frame {{ background:{P['bg']}; border:1px solid {P['frame']}; border-radius:{r}px; }}"
            + self._frame_extra)

    def _jar_btn_style(self, on):
        if on:
            _ss(self.btn_jar, "background:#3b0764; color:white; border:1px solid #cc44ff; border-radius:8px; padding:10px; font-weight:900; font-size:12px;")
        else:
            _ss(self.btn_jar, "background:#1a1a1a; color:#cc88ff; border:1px solid #440088; border-radius:8px; padding:10px; font-weight:900; font-size:12px;")

    def _toggle_max(self):
        if self.isMaximized(): self.showNormal()
        else: self.showMaximized()

    def changeEvent(self, event):
        super().changeEvent(event)
        if event.type() == event.WindowStateChange and hasattr(self, "frame"):
            self._apply_frame_style()
            self.btn_max.setText("❐" if self.isMaximized() else "□")

    def eventFilter(self, obj, event):
        if obj is getattr(self, "header", None):
            t = event.type()
            if t == event.MouseButtonDblClick and event.button() == Qt.LeftButton:
                self._toggle_max(); return True
            if t == event.MouseButtonPress and event.button() == Qt.LeftButton:
                self._drag_pos = event.globalPos() - self.frameGeometry().topLeft(); return True
            if t == event.MouseMove and self._drag_pos is not None and event.buttons() & Qt.LeftButton:
                if self.isMaximized():
                    self.showNormal()
                    self._drag_pos = QPointF(self.width() / 2, 36).toPoint()
                self.move(event.globalPos() - self._drag_pos); return True
            if t == event.MouseButtonRelease:
                self._drag_pos = None
        return super().eventFilter(obj, event)

    # ── remember username / preset / voice between runs ─────────
    def _restore_state(self):
        st = self._state if isinstance(self._state, dict) else {}
        self.user_in.setText(str(st.get("username", "")))
        vi = self.voice_box.findData(st.get("voice", VOICE_MODE_THAI_SOFT))
        if vi >= 0 and vi != self.voice_box.currentIndex():
            self.voice_box.setCurrentIndex(vi)
        pi = int(st.get("preset", 0)) if str(st.get("preset", 0)).isdigit() else 0
        if 0 < pi < len(self._presets):
            self.preset_box.setCurrentIndex(pi)   # emits apply_preset
        else:
            self.apply_preset(0)
        self._sync_win_overlay_from_preset(self._active_preset_idx)
        self.add_log(T("main_title") + "  •  " + THEME_NAME, P["muted"])

    def _save_state(self):
        _write_json(STATE_FILE, {
            "username": self.user_in.text().strip().replace("@", ""),
            "voice":    self.voice_box.currentData(),
            "preset":   self._active_preset_idx,
        })

    # ── 🆕 jar helpers ───────────────────────────────────────────
    def _on_jar_scale(self, val):
        self.lbl_jar_pct.setText(f"{val}%")
        if hasattr(self, "gift_jar"):
            self.gift_jar.set_scale(val)
        s = load_jar_settings()
        s["scale"] = val
        save_jar_settings(s)

    def _jar_test_drop(self):
        for i in range(5):
            QTimer.singleShot(i * 100, self.gift_jar.add_gift)

    def _jar_clear(self):
        self.gift_jar.clear_particles()

    def _update_jar_count(self):
        if hasattr(self, "gift_jar") and hasattr(self, "lbl_jar_count"):
            n = self.gift_jar.particle_count()
            self.lbl_jar_count.setText(f"{n} gifts")

    def toggle_jar_overlay(self):
        if self.gift_jar.isVisible():
            self.gift_jar.hide()
            self._jar_btn_style(False)
        else:
            self.gift_jar.show()
            self.gift_jar.raise_()
            self._jar_btn_style(True)

    # ── lang ─────────────────────────────────────────────────────
    def _toggle_lang(self):
        global _LANG
        _LANG = _next_lang()   # วน TH→EN→FIL→RU→TH
        self._refresh_ui_lang()

    def _refresh_ui_lang(self):
        self.setWindowTitle(T("main_title"))
        # ปุ่มแสดงชื่อ + flag ภาษาปัจจุบัน
        self.btn_lang.setText(_lang_button_label())
        self.lbl_preset.setText(T("preset_label"))
        self.btn_settings.setText(T("settings_btn"))
        self.lbl_voice.setText(T("voice_label"))
        self.voice_box.blockSignals(True)
        cur_data = self.voice_box.currentData()
        self.voice_box.clear()
        self.voice_box.addItem(T("voice_off"),    VOICE_MODE_OFF)
        self.voice_box.addItem(T("voice_thai_m"), VOICE_MODE_THAI)
        self.voice_box.addItem(T("voice_thai_f"), VOICE_MODE_THAI_SOFT)
        idx = self.voice_box.findData(cur_data)
        if idx >= 0: self.voice_box.setCurrentIndex(idx)
        self.voice_box.blockSignals(False)
        self.user_in.setPlaceholderText(T("username_placeholder"))
        self.btn.setText(T("disconnect_btn") if self.tt.is_running() else T("connect_btn"))
        self.btn_top_like.setText(T("top_likers_btn"))
        self.btn_top_gift.setText(T("top_gifters_btn"))
        self.btn_win_overlay.setText(T("win_counter_btn"))
        self.btn_jar.setText(T("jar_btn"))
        self.status_badge.setText(T("status_ready"))
        self.lbl_logs.setText(T("logs_label"))
        self.lbl_sec_conn.setText(T("sec_connect"))
        self.lbl_sec_over.setText(T("sec_overlays"))
        self.lbl_sec_jar.setText(T("sec_jar"))
        self.lbl_sub.setText(T("header_sub"))
        self.btn_clear_logs.setText(T("clear_logs"))
        self.lbl_tip.setText(T("tip"))
        self.apply_preset(self._active_preset_idx)

    def _rebuild_preset_combo(self):
        self.preset_box.blockSignals(True)
        self.preset_box.clear()
        for i, p in enumerate(self._presets):
            self.preset_box.addItem(f"{i+1}. {p.get('name','Preset')}")
        self.preset_box.blockSignals(False)

    def _sync_win_overlay_from_preset(self, idx: int):
        """อ่าน win config จาก gift_map (format ใหม่) แล้วส่งให้ WinCounterOverlay"""
        if not (0 <= idx < len(self._presets)):
            return
        p    = self._presets[idx]
        bi   = int(p.get("win_bg_idx", 0))
        opts = get_overlay_bg_options()
        _, bg_col, is_ch = opts[bi] if 0 <= bi < len(opts) else opts[0]

        # อ่าน win rules จาก gift_map format ใหม่
        gift_win_rules = []
        for g_name, v in (p.get("gift_map") or {}).items():
            if isinstance(v, dict) and v.get("win_enabled"):
                gift_win_rules.append({
                    "gift_name":   g_name,
                    "win_enabled": True,
                    "win_delta":   int(v.get("win_delta", 1)),
                    "key_enabled": bool(v.get("key", "")),
                    "key_seq":     v.get("key", ""),
                })

        win_cfg = {
            "hotkey_up":          p.get("win_hk_up",  "up"),
            "hotkey_dn":          p.get("win_hk_dn",  "down"),
            "hotkey_reset":       p.get("win_hk_rst", ""),
            "max_win":            int(p.get("win_max", 10)),
            "overlay_bg":         bg_col,
            "overlay_chroma":     is_ch,
            "overlay_text_color": p.get("win_tc", "#ffffff"),
            "overlay_font_size":  int(p.get("win_font_size", 28)),
            "gift_win_rules":     gift_win_rules,
        }
        self.win_overlay.apply_settings_from_preset(win_cfg)

    def open_settings(self):
        cur = self.preset_box.currentIndex()
        dlg = SettingsDialog(self._presets, cur, parent=self)
        dlg.presets_changed.connect(self._on_presets_saved)
        dlg.exec_()

    def _on_presets_saved(self, new_presets):
        self._presets  = new_presets
        old_idx        = self.preset_box.currentIndex()
        self._rebuild_preset_combo()
        new_idx        = min(old_idx, len(self._presets) - 1)
        self.preset_box.setCurrentIndex(new_idx)
        self.apply_preset(new_idx)
        save_presets(self._presets)
        self.add_log(T("log_presets_saved", n=len(self._presets)), "#ffd700")
        # sync win settings จาก preset ที่ active
        self._sync_win_overlay_from_preset(new_idx)

    def apply_preset(self, idx):
        if not (0 <= idx < len(self._presets)): return
        self._active_preset_idx = idx
        p = self._presets[idx]
        # gift_map format ใหม่: {name_lower: {"key":str,"win_enabled":bool,"win_delta":int}}
        # normalize key names to lowercase, keep values as-is
        raw_map = p.get("gift_map", {})
        gift_map = {}
        for k, v in raw_map.items():
            gift_map[norm_name(k)] = v
        follow_enabled = bool(p.get("follow_enabled", True))
        follow_seq     = p.get("follow_seq", "r")
        follow_once    = bool(p.get("follow_once", True))
        like_enabled   = bool(p.get("like_enabled", True))
        like_seq       = p.get("like_seq", "v")
        like_target    = int(p.get("like_target", 100))
        fo_txt    = T("follow_once_yes") if follow_once else T("follow_once_no")
        fon_str   = ("✅ " if follow_enabled else "⛔ ") + follow_seq
        lon_str   = ("✅ " if like_enabled   else "⛔ ") + like_seq
        self.preset_hint.setText(
            T("preset_hint", n=idx+1, fseq=fon_str, fonce=fo_txt,
              ltarget=like_target, lseq=lon_str, ngifts=len(gift_map)))
        self.add_log(T("log_preset_change", name=p.get("name", "")), "#25f4ee")
        if hasattr(self, "tt") and self.tt is not None:
            self.tt.set_runtime(
                gift_map, follow_enabled, follow_seq, follow_once,
                like_enabled, like_seq, like_target)

    def open_top_likers(self):
        like_list = sorted(self.tt.like_scores.values(),
                           key=lambda x: int(x.get("score", 0)), reverse=True)[:5]
        self.win_likers.update_list(like_list, self.avatar_provider)
        self.win_likers.show(); self.win_likers.raise_(); self.win_likers.activateWindow()

    def open_top_gifters(self):
        gift_list = sorted(self.tt.gift_coins.values(),
                           key=lambda x: int(x.get("coins", 0)), reverse=True)[:5]
        self.win_gifters.update_list(gift_list, self.avatar_provider)
        self.win_gifters.show(); self.win_gifters.raise_(); self.win_gifters.activateWindow()

    def _on_top_like_update(self, like_list):
        pass  # timer-based update เท่านั้น ป้องกัน double-update

    def _on_top_gift_update(self, gift_list):
        pass  # timer-based update เท่านั้น ป้องกัน double-update

    def on_voice_change(self):
        mode = self.voice_box.currentData()
        self.voice_mode = mode
        self.tt.set_voice_mode(mode)
        try:
            while True: self.tts_q.get_nowait(); self.tts_q.task_done()
        except Exception: pass
        voice_labels = {
            VOICE_MODE_OFF:       T("voice_off"),
            VOICE_MODE_THAI:      T("voice_thai_m"),
            VOICE_MODE_THAI_SOFT: T("voice_thai_f"),
        }
        self.add_log(T("log_voice_change", label=voice_labels.get(mode, mode)), "#aaaaaa")

    def add_log(self, text, color="#00ff00"):
        ts = time.strftime("%H:%M:%S")
        if not hasattr(self, "logs") or self.logs is None:
            print(f"[{ts}] {text}"); return
        self.logs.append(
            f'<span style="color:{P["dim"]}">[{ts}]</span> <span style="color:{color}">{text}</span>')

    def update_status_ui(self, text, style):
        c = {"success": "#22c55e", "loading": "#f59e0b", "error": "#fe2c55"}.get(style, "#555")
        c = qss(c)
        self.status_badge.setText(text)
        _ss(self.status_badge, 
            f"background:{P['panel']}; color:{c}; border:1px solid {c}; border-radius:15px; padding:6px 14px; font-weight:700;")

    def press_worker(self):
        while not self.stop_worker.is_set():
            try: seq, count, source = self.press_q.get(timeout=0.2)
            except queue.Empty: continue
            seq = (seq or "").strip()
            if not seq or count <= 0:
                self.press_q.task_done(); continue
            try:
                for _ in range(int(count)):
                    for ch in seq:
                        pydirectinput.keyDown(ch); pydirectinput.keyUp(ch)
                        if KEY_DELAY > 0: time.sleep(KEY_DELAY)
                    if REPEAT_DELAY > 0: time.sleep(REPEAT_DELAY)
            finally:
                self.press_q.task_done()

    def tts_worker(self):
        engine = None
        if (not HAS_EDGE_TTS) and HAS_TTS:
            engine = pyttsx3.init()
            try: engine.setProperty("rate", 185)
            except Exception: pass
        if HAS_EDGE_TTS:
            try: pygame.mixer.init()
            except Exception: pass

        def pick_voice(mode):
            return EDGE_VOICE_THAI if mode == VOICE_MODE_THAI else EDGE_VOICE_THAI_SOFT

        def speak_edge(text, mode):
            out_path = None
            try:
                voice = pick_voice(mode)
                with tempfile.NamedTemporaryFile(delete=False, suffix=".mp3") as f:
                    out_path = f.name
                async def _gen():
                    comm = edge_tts.Communicate(text=text, voice=voice)
                    await comm.save(out_path)
                asyncio.run(_gen())
                pygame.mixer.music.load(out_path)
                pygame.mixer.music.play()
                while pygame.mixer.music.get_busy() and not self.stop_tts.is_set():
                    time.sleep(0.05)
            except Exception: pass
            finally:
                try:
                    if out_path and os.path.exists(out_path):
                        os.remove(out_path)
                except Exception: pass

        while not self.stop_tts.is_set():
            try: text = self.tts_q.get(timeout=0.2)
            except queue.Empty: continue
            try:
                if not text or self.voice_mode == VOICE_MODE_OFF: continue
                if HAS_EDGE_TTS: speak_edge(str(text), self.voice_mode)
                elif engine is not None: engine.say(str(text)); engine.runAndWait()
            except Exception: pass
            finally: self.tts_q.task_done()

    def toggle_system(self):
        if self.tt.is_running():
            self.add_log(T("log_disconnect_req"), "#aaaaaa")
            self.tt.stop()
            self.btn.setText(T("connect_btn"))
            _ss(self.btn, 
                "background:#fe2c55; color:white; border-radius:8px;"
                " padding:10px 22px; font-weight:900; font-size:14px;")
            self.user_in.setEnabled(True)
            self.update_status_ui(T("status_ready"), "error")
            return

        u = self.user_in.text().strip().replace("@", "")
        if not u:
            self.add_log(T("log_no_username"), "#ff3333"); return

        self.apply_preset(self._active_preset_idx)
        self._save_state()
        self.tt.set_voice_mode(self.voice_mode)
        self.user_in.setEnabled(False)
        self.btn.setText(T("disconnect_btn"))
        _ss(self.btn, 
            "background:#1a1a1a; color:#fe2c55; border-radius:8px;"
            " padding:10px 22px; font-weight:900; font-size:14px; border:2px solid #fe2c55;")
        self.tt.start(u)

    def toggle_win_overlay(self):
        if self.win_overlay.isVisible():
            self.win_overlay.hide()
        else:
            self.win_overlay.show(); self.win_overlay.raise_(); self.win_overlay.activateWindow()

    def closeEvent(self, event):
        self._save_state()
        try: self.win_overlay.close()
        except Exception: pass
        try: self.gift_jar.close()
        except Exception: pass
        try: self.tt.stop()
        except Exception: pass
        self.stop_worker.set()
        self.stop_tts.set()
        event.accept()


# ══════════════════════════════════════════════════════════════════
#  ENTRY POINT
# ══════════════════════════════════════════════════════════════════
def main():
    QLocale.setDefault(QLocale(QLocale.English, QLocale.UnitedStates))
    app = QApplication(sys.argv)
    app.setFont(QFont("Segoe UI", 10))

    ico_path = None
    try:
        ico_path = os.path.join(RES_DIR, "app.ico")
        if os.path.exists(ico_path):
            app.setWindowIcon(QIcon(ico_path))
        else:
            ico_path = None
    except Exception:
        ico_path = None

    window = MainWindow()
    if ico_path:
        try: window.setWindowIcon(QIcon(ico_path))
        except Exception: pass
    window.show()
    sys.exit(app.exec_())


if __name__ == "__main__":
    main()