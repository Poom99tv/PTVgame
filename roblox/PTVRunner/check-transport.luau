local Http=game:GetService("HttpService")
local source=game.ServerScriptService.LiveBridgeTransport.Source
local calls={};local attributes={}
local race={SetAttribute=function(_,k,v)attributes[k]=v end,GetAttribute=function(_,k)return attributes[k]end}
local fakeGame={PlaceId=71880688656931,JobId="mock-room",PrivateServerOwnerId=0,CreatorId=123,CreatorType=Enum.CreatorType.User}
fakeGame.ServerStorage={WaitForChild=function()return {}end}
function fakeGame:GetService(name)
 if name=="RunService" then return {IsStudio=function()return true end}end
 if name=="Players" then return {PlayerRemoving={Connect=function()return {Disconnect=function()end}end}}end
 if name=="HttpService" then return {
  GenerateGUID=function()return "test-id"end,JSONEncode=function(_,v)return Http:JSONEncode(v)end,JSONDecode=function(_,v)return Http:JSONDecode(v)end,
  RequestAsync=function(_,request)
   table.insert(calls,request)
   assert(not request.Url:find("/ui/skills",1,true),"Relay must not receive skill settings")
   local payload
   if request.Url:find("/game/pair",1,true)then payload={ok=true,protocol=8,sequence=0,status="Connected",placeId="71880688656931",settingsAuthority="game"}
   elseif request.Url:find("/game/events",1,true)then payload={session="signal-test",status="Connected",events={{seq=1,kind="gift",giftKey="1dc1fd9347876fbd",amount=300,viewer="viewer",viewerDisplayName="Viewer",receivedAt=DateTime.now().UnixTimestampMillis}}}
   else payload={ok=true}end
   return {Success=true,StatusCode=200,Body=Http:JSONEncode(payload)}
  end
 }end
 error(name)
end
local f,err=loadstring(source);assert(f,err)
setfenv(f,setmetatable({game=fakeGame,workspace={WaitForChild=function()return race end},require=function()return {Url="http://127.0.0.1:8767",Token=""}end},{__index=getfenv()}))
local transport=f()
local player={UserId=123,Parent=true}
local paired=transport.Pair(player,"GTE1|unit-test|"..string.rep("a",64),{version=3,skills={}})
assert(paired.ok,paired.error)
local count=#calls;assert(transport.Save({version=3,skills={}}));assert(#calls==count,"Skill settings must stay in game")
local packet=transport.Poll(0);assert(packet.events[1].amount==300 and packet.events[1].giftKey=="1dc1fd9347876fbd")
local request=calls[#calls];assert(request.Headers["X-Race-Place"]=="71880688656931")
assert(request.Headers["X-Race-Room"]=="mock-room" and request.Headers["X-Race-Owner"]=="123")
fakeGame.PlaceId=999;assert(not transport.Pair(player,"GTE1|unit-test|"..string.rep("a",64),{}).ok)
return {paired=true,rawGiftAmount=packet.events[1].amount,skillSettingsSent=false,wrongMapRejected=true,roomHeaders=true}
