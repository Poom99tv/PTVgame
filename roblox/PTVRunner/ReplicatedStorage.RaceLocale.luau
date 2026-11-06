local Players=game:GetService('Players')
local M={}
local pairsText={
 {'Leaderboard','อันดับการแข่งขัน'},{'Rank','อันดับ'},{'TikTok name','ชื่อ TikTok'},{'Speed (m/s)','ความเร็ว (m/s)'},{'Boost (m/s)','เพิ่ม (m/s)'},{'AUTO','อัตโนมัติ'},{'No racers yet','ยังไม่มีนักแข่ง'},{'Watching','กำลังติดตาม'},
 {'Skill settings','ตั้งค่าสกิล'},{'Save','บันทึก'},{'Saving…','กำลังบันทึก…'},{'Saved','บันทึกแล้ว'},{'Gift','ของขวัญ'},{'Follow (once/person)','ติดตาม (1 ครั้ง/คน)'},{'Heart Me 🧡','ใจส้ม 🧡'},{'Likes 👆','เคาะจอ 👆'},
 {'🚀 Speed boost','🚀 เพิ่มความเร็ว'},{'⚡ FLASH lightning','⚡ FLASH สายฟ้า'},{'🌀 Warp ahead of leader','🌀 วาร์ปแซงที่ 1'},{'🕳 Send leader to last','🕳 ดูดที่ 1 ไปท้ายสุด'},
 {'Speed added per use (%)','ความเร็วเพิ่มต่อครั้ง (%)'},{'Speed multiplier (×)','ตัวคูณความเร็ว (เท่า)'},{'Duration (seconds)','ระยะเวลา (วินาที)'},{'Distance ahead of leader (m)','โผล่หน้าที่ 1 (เมตร)'},{'Distance behind last racer (m)','ส่งไปหลังคนสุดท้าย (เมตร)'},
 {'Activate this skill when (multiple allowed)','ใช้สกิลนี้เมื่อ (ติ๊กได้หลายอย่าง)'},{'Choose a gift','เลือกของขวัญ'},
 {'Choose gift • Price: low → high','เลือกของขวัญ • ราคาเพชร น้อย → มาก'},
 {'Search: Rose, Heart Me, GG','ค้นหาชื่อ เช่น Rose, Heart Me, GG'},
 {'Heart Me = orange-heart gift  •  Likes = each viewer’s screen taps\nx = number needed for one skill activation','ใจส้ม = ของขวัญ Heart Me  •  เคาะจอ = กดหัวใจสะสมของแต่ละคน\nตัวเลขหลัง x = ต้องครบกี่ครั้งถึงใช้สกิล 1 ครั้ง'},
 {'Could not load settings','โหลดการตั้งค่าไม่สำเร็จ'},{'Could not save','บันทึกไม่สำเร็จ'},{'Only the game controller can edit settings','เฉพาะผู้ควบคุมเกมเท่านั้น'},
 {'Invalid settings','ข้อมูลตั้งค่าไม่ถูกต้อง'},{'Missing skill settings','ข้อมูลสกิลไม่ครบ'},{'Gift-triggered skills must use different gifts','สกิลที่ใช้ของขวัญ ต้องเลือกของขวัญไม่ซ้ำกัน'},
 {'Bridge is offline. Open TikTokBridge.exe','เชื่อมโปรแกรมไม่ได้ กรุณาเปิด TikTokBridge.exe'},
 {'✨ SPACE PORTAL ✨','✨ ประตูวาร์ปอวกาศ ✨'},{' opens a portal!',' เปิดประตูวาร์ป!'},{'Bye-bye~ 👋','บ๊ายบาย~ 👋'},
 {' jumps into the portal!',' กระโดดเข้าประตู!'},{'Warp ahead of ','วาร์ปแซงหน้า '},{'Warping into the lead!','วาร์ปพุ่งนำ!'},
 {'🌀 LEADER PULL PORTAL 🌀','🌀 ประตูวาร์ปดูดอันดับ 1 🌀'},{' sends a portal to pull ',' ส่งประตูวาร์ปไปดูด '},
 {'the leader','ที่ 1'},{' struggles to escape!',' ดิ้นสุดแรง!'},{'WHOOSH! 🌀','ฟึ่บบบ! 🌀'},{' was sent to last place! 😵',' ถูกส่งไปท้ายสุด! 😵'},
 {'🚀 Speed boost','🚀 เร่งความเร็ว'},{'⚡ FLASH lightning!','⚡ FLASH สายฟ้า!'},{'🌀 WARP!','🌀 วาร์ป!'},{'✨ Skill activated','✨ ใช้สกิล'},
 {'⏳ Waiting to run','⏳ รอออกวิ่ง'},{'⏳ ⚡ FLASH ready','⏳ ⚡ FLASH รอออกวิ่ง'},
 {'⏳ 🌀 Lead warp queued','⏳ 🌀 วาร์ปแซงที่ 1 รอคิว'},{'⏳ 🕳 Leader pull queued','⏳ 🕳 ดูดที่ 1 รอคิว'},
 {'⏳ Queued  ','⏳ รอคิว  '},{'Count must be between ','จำนวนต้องอยู่ระหว่าง '},{'must be between','ต้องอยู่ระหว่าง'},
 -- mud pit / freeze / sonic dash skills (settings cards)
 {'🟤 Mud trap ahead','🟤 หลุมโคลนดักหน้า'},{'🧊 Freeze everyone','🧊 แช่แข็งทุกคน'},{'💨 Sonic dash','💨 บินพุ่งลมโซนิค'},
 {'Freeze time (seconds)','แข็งนาน (วินาที)'},{'Dash distance (m)','บินไปข้างหน้า (เมตร)'},{' : please choose a gift',' : กรุณาเลือกของขวัญ'},
 {'Applied in game • could not reach TikTokBridge.exe (open it and press Save again to keep these settings)','ใช้ค่าใหม่ในเกมแล้ว • แต่ส่งให้ TikTokBridge.exe ไม่ได้ (เปิดโปรแกรมแล้วกดบันทึกอีกครั้งเพื่อจำค่า)'},
 {'Invalid request','คำขอไม่ถูกต้อง'},{'Please wait a moment','กรุณารอสักครู่'},
 -- mud pit cutscene
 {'🟤 MUD TRAP 🟤','🟤 หลุมโคลนดักหน้า 🟤'},{' sets a mud trap for ',' วางหลุมโคลนดัก '},
 {'🟤 STUCK IN THE MUD! 🟤','🟤 ติดหลุมโคลน! 🟤'},{' stepped into the mud pit!',' เหยียบโดนหลุมโคลน!'},
 {' is clawing to get out!',' ตะเกียกตะกายสุดชีวิต!'},{'Sank out of sight... 🫧','จมหายไปแล้ว... 🫧'},
 {' is back in the race! All boosts kept 💪',' กลับมาวิ่งต่อ! พลังยังอยู่ครบ 💪'},
 -- freeze cutscene
 {'🧊 FREEZE! 🧊','🧊 แช่แข็ง! 🧊'},{' casts an ice spell on ',' ร่ายเวทน้ำแข็งใส่ '},{' Huh!? What is that!?',' เอ๊ะ!? อะไรน่ะ!'},
 {'Hands... feet... freezing solid!? 🥶','มือ... เท้า... แข็งไปหมดแล้ว!? 🥶'},{' is frozen solid! 🧊',' แข็งทั้งตัว! 🧊'},
 {'🧊 FREEZE EVERYONE 🧊','🧊 แช่แข็งทุกคน 🧊'},{'❄️ Everyone is frozen! Except ','❄️ ทุกคนถูกแช่แข็ง! ยกเว้น '},
 -- skill pop-ups and queue badges
 {'🧊 Freeze everyone!','🧊 แช่แข็งทุกคน!'},{'💨 Sonic dash!','💨 บินพุ่ง!'},{'🟤 Mud trap!','🟤 หลุมโคลน!'},
 {'⏳ 🟤 Mud trap queued','⏳ 🟤 หลุมโคลนรอคิว'},{'⏳ 🧊 Freeze queued','⏳ 🧊 แช่แข็งรอคิว'},{'⏳ 💨 Sonic dash queued','⏳ 💨 บินพุ่งรอคิว'},
 -- NEW FOLLOW
 {'  Thanks for the follow 💛','  ขอบคุณที่ติดตาม 💛'},{' and ',' และอีก '},{' more',' คน'},
 -- language buttons
 {'Language','ภาษา'},
 -- winners' podium
 {'🏆 RACE WINNERS 🏆','🏆 ผู้ชนะการแข่งขัน 🏆'},{' climbs out of the mud!',' โผล่ขึ้นมาจากโคลน!'},{' drops in from the sky!',' ร่วงลงมาจากฟ้า!'},
 {' arrives through a warp portal!',' มาถึงทางประตูวาร์ป!'},{'🥉 3rd place  ','🥉 อันดับ 3  '},{'🥈 2nd place  ','🥈 อันดับ 2  '},{'🥇 1st place  ','🥇 อันดับ 1  '},
 {'🎉 Congratulations to the winners! 🎉','🎉 ยินดีด้วยกับผู้ชนะ! 🎉'},{'Next race starts soon…','รอบใหม่กำลังจะเริ่ม…'},
 {'🔊 Game sound','🔊 เสียงในเกม'},{'Applies instantly','ปรับแล้วใช้ทันที'},{'Master volume','เสียงทั้งหมด'},{'Effects','เอฟเฟกต์'},{'Ambience','เสียงบรรยากาศ'},{'Background music','เพลงพื้นหลัง'},{'Footsteps','เสียงเท้า'},{'📐 Screen layout','📐 ขนาดบนจอ'},{'Leaderboard size','ขนาดตารางอันดับ'},{'Leaderboard rows','แถวตารางอันดับ'},{'Minimap size','ขนาดมินิแมพ'},
}
table.insert(pairsText,{'Pair TikTokBridge','จับคู่ TikTokBridge'})
table.insert(pairsText,{'Pair','จับคู่'})
table.insert(pairsText,{'Disconnect game','ตัดการเชื่อมเกม'})
table.insert(pairsText,{'Close','ปิด'})
table.insert(pairsText,{'Open TikTokBridge v7 → Connect → Copy pairing code. Paste it below. Private server owner only.','เปิด TikTokBridge v7 → เชื่อมต่อ → คัดลอกรหัสจับคู่ แล้ววางด้านล่าง เฉพาะเจ้าของห้องส่วนตัว'})
table.insert(pairsText,{'Pair Game To Earn','จับคู่ Game To Earn'})
table.insert(pairsText,{'Open PTVgame Launcher → Game To Earn → PTVRunner → Copy pairing code. Skill settings stay in this game. Private server owner only.','เปิด PTVgame Launcher → Game To Earn → PTVRunner → คัดลอกรหัสจับคู่ ตั้งค่าสกิลในเกมนี้ เฉพาะเจ้าของห้องส่วนตัว'})
local english,thai={},{}
for _,p in pairsText do english[p[2]]=p[1];thai[p[1]]=p[2]end
-- longest Thai phrases are replaced first, so a short word never breaks a longer sentence
local bySize=table.clone(pairsText)
table.sort(bySize,function(a,b)return #a[2]>#b[2]end)
local function player()return Players.LocalPlayer end
function M.Language()return player():GetAttribute('RaceLanguage')or 'en'end
function M.Set(lang)if lang=='en'or lang=='th'then player():SetAttribute('RaceLanguage',lang)end end
function M.Text(value)
 local lang=M.Language();local exact;if lang=='en'then exact=english[value]else exact=thai[value]end;if exact then return exact end
 local a,b,c=value:match('^(%d+) / (%d+)  •  (%d+) ของขวัญ$')
 if a then return lang=='en'and (a..' / '..b..'  •  '..c..' gifts')or value end
 a,b,c=value:match('^(%d+) / (%d+)  •  (%d+) gifts$')
 if a then return lang=='th'and (a..' / '..b..'  •  '..c..' ของขวัญ')or value end
 if lang=='en'then
  value=value:gsub('เลือกของขวัญ • ','Choose gift • ')
  for _,p in bySize do
   if #p[2]>3 then
    local start=value:find(p[2],1,true)
    while start do
     value=value:sub(1,start-1)..p[1]..value:sub(start+#p[2])
     start=value:find(p[2],start+#p[1],true)
    end
   end
  end
 end
 return value
end
function M.Bind(object,value)
 object:SetAttribute('LocaleKey',value)
 local function update()object.Text=M.Text(value)end
 update();local c=player():GetAttributeChangedSignal('RaceLanguage'):Connect(update)
 object.Destroying:Once(function()c:Disconnect()end)
end
return M


