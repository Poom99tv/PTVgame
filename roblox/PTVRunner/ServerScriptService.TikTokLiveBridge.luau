local Http=game:GetService("HttpService")
local RunService=game:GetService("RunService")
local Lobby=require(script.Parent:WaitForChild("LiveAvatarLobby"))
Lobby.Start()
local Skills=require(script.Parent:WaitForChild("RaceSkills"))
local Transport=require(script.Parent:WaitForChild("LiveBridgeTransport"))
local race=workspace:WaitForChild("FourBiomeRace")
local cursor=0;local session=nil
local started=DateTime.now().UnixTimestampMillis
local enroll=Instance.new("BindableFunction");enroll.Name="ApplyRaceSkill";enroll.Parent=game.ServerStorage
enroll.OnInvoke=function(viewer,skill,amount,id)return Skills.Apply(viewer,skill,amount,id)end
-- test hook: feed a raw TikTok event ({kind="gift"/"like"/"follow", viewer=..., giftKey=..., amount=...})
-- NEW FOLLOW effect on every screen (NewFollowFx)
local followFx=Instance.new("RemoteEvent");followFx.Name="RaceFollowFx";followFx.Parent=game.ReplicatedStorage
local function onFollow(event)followFx:FireAllClients(tostring(event.viewerDisplayName or event.viewer or ""))end
local simulate=Instance.new("BindableFunction");simulate.Name="SimulateTikTokEvent";simulate.Parent=game.ServerStorage
simulate.OnInvoke=function(event)if type(event)=="table"and event.kind=="follow"then onFollow(event)end;return Skills.Trigger(event)end
local transportGeneration=Transport.Generation or 0
while task.wait(.5)do
 if transportGeneration~=(Transport.Generation or 0)then transportGeneration=Transport.Generation or 0;cursor=Transport.StartCursor or 0;session=nil end
 local ok,data=pcall(Transport.Poll,cursor)
 if ok and not data then continue end
 if ok then
  if session and session~=data.session then cursor=0 end
  session=data.session
  -- Skill mappings always come from the in-game settings, including Studio.
  -- Game To Earn only carries raw viewer actions; it never configures skills.
  race:SetAttribute("TikTokBridgeStatus",data.status or "Connected")
  for _,event in ipairs(data.events or {})do
   if type(event.seq)=="number"and event.seq>cursor then
    if (tonumber(event.receivedAt)or 0)<(race:GetAttribute("EnrollmentOpenedAtMillis")or started)then
     -- Consume old comments without enrolling last round\'s racers again.
    elseif event.kind=="boost"then
     -- older bridge builds map gifts/likes themselves; respect the in-game on/off switches
     if Skills.LegacyAllowed(event)then Skills.Apply(tostring(event.viewer),event.skill,event.amount,tostring(session)..":"..event.seq,event.viewerDisplayName)end
    elseif event.kind=="gift"or event.kind=="like"or event.kind=="follow"then
     if event.kind=="follow"then onFollow(event)end
     Skills.Trigger(event,tostring(session)..":"..event.seq)
    elseif type(event.account)=="string"then
     local result=Lobby.Enqueue(event.account,tostring(event.viewer),event.viewerDisplayName);if result.full then break end
    end
    cursor=event.seq
   end
  end
 else race:SetAttribute("TikTokBridgeStatus","Bridge offline: "..tostring(data):sub(1,150))end
end


