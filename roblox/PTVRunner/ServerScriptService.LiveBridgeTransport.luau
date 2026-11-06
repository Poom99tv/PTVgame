-- HTTPS pairing is held only in this server. Credentials never replicate to spectators.
local Http=game:GetService('HttpService')
local Run=game:GetService('RunService')
local Players=game:GetService('Players')
local race=workspace:WaitForChild('FourBiomeRace')
local M={};local active=nil;local pending=false;local owner=0
local room=game.JobId~=''and game.JobId or 'studio-'..Http:GenerateGUID(false)
local legacy=require(game.ServerStorage:WaitForChild('LiveBridgeConfig'))
function M.CanControl(player)
 if Run:IsStudio()then return true end
 if game.PrivateServerOwnerId>0 then return player.UserId==game.PrivateServerOwnerId end
 if game.CreatorType==Enum.CreatorType.User then return player.UserId==game.CreatorId end
 local ok,rank=pcall(function()return player:GetRankInGroupAsync(game.CreatorId)end)
 return ok and rank>=254
end
function M.ProfileId()
 if game.PrivateServerOwnerId>0 then return game.PrivateServerOwnerId end
 if game.CreatorType==Enum.CreatorType.User then return game.CreatorId end
 return nil
end
local function request(connection,path,body)
 local response=Http:RequestAsync({Url=connection.url..path,Method=body and 'POST'or 'GET',Headers={Authorization='Bearer '..connection.token,['Content-Type']='application/json',['X-Race-Room']=room,['X-Race-Owner']=tostring(connection.owner or 0),['X-Race-Place']=tostring(game.PlaceId)},Body=body and Http:JSONEncode(body)or nil})
 assert(response.Success,'HTTPS '..tostring(response.StatusCode)..': '..response.Body:sub(1,180))
 return Http:JSONDecode(response.Body)
end
function M.Status()
 return {paired=active~=nil,ownerId=owner,status=race:GetAttribute('TikTokBridgeStatus')or 'Not paired',isPrivate=game.PrivateServerOwnerId>0,isStudio=Run:IsStudio()}
end
function M.Pair(player,code,settings)
 if not M.CanControl(player)then return {ok=false,error='Only the private server owner can pair this room'}end
 if pending then return {ok=false,error='Pairing in progress'}end
 if type(code)~='string'or #code>512 then return {ok=false,error='Invalid pairing code'}end
 local host,token=code:match('^GTE1|([a-z0-9%-]+)|([a-fA-F0-9]+)$')
 local signalOnly=host~=nil
 if signalOnly and game.PlaceId~=71880688656931 then return {ok=false,error='This connection is only for PTVRunner'}end
 if not signalOnly then host,token=code:match('^IR7|([a-z0-9%-]+)|([a-fA-F0-9]+)$')end
 if not host or #host<3 or #host>100 or #token~=64 then return {ok=false,error='Copy the pairing code from Game To Earn in the Launcher'}end
 pending=true
 local candidate={url='https://'..host..'.trycloudflare.com',token=token,owner=player.UserId,signalOnly=signalOnly}
 local ok,result=pcall(function()
  local reply=request(candidate,'/game/pair',{room=room,owner=player.UserId,placeId=tostring(game.PlaceId)})
  assert(reply.ok and reply.protocol==(signalOnly and 8 or 7),'Update Game To Earn in the Launcher')
  if signalOnly then
   assert(tostring(reply.placeId)==tostring(game.PlaceId)and reply.settingsAuthority=='game','Wrong PTVRunner connection')
  else request(candidate,'/ui/skills',settings)end
  return reply
 end)
 pending=false
 if not ok then return {ok=false,error='Pairing failed: '..tostring(result):sub(1,220)}end
 if not player.Parent then pcall(request,candidate,'/game/unpair',{});return {ok=false,error='Owner left'}end
 if active and active.url~=candidate.url then pcall(request,active,'/game/unpair',{})end
 active=candidate;owner=player.UserId;M.Generation=(M.Generation or 0)+1;M.StartCursor=tonumber(result.sequence)or 0
 race:SetAttribute('TikTokBridgeStatus',result.status or 'Paired')
 race:SetAttribute('TikTokBridgeOwnerId',owner)
 return {ok=true,status=M.Status()}
end
function M.Unpair(player)
 if player and not M.CanControl(player)then return {ok=false,error='Only the room owner can disconnect'}end
 local previous=active;active=nil;owner=0;M.Generation=(M.Generation or 0)+1;M.StartCursor=0
 race:SetAttribute('TikTokBridgeStatus','Not paired');race:SetAttribute('TikTokBridgeOwnerId',nil)
 if previous then task.spawn(function()pcall(request,previous,'/game/unpair',{})end)end
 return {ok=true,status=M.Status()}
end
function M.Poll(cursor)
 local connection=active
 if not connection and Run:IsStudio()then connection={url=legacy.Url,token=legacy.Token}end
 if not connection then return nil end
 local data=request(connection,(active and '/game/events?after='or '/events?after=')..cursor)
 if active~=connection and not Run:IsStudio()then return nil end
 return data
end
function M.Save(settings)
 local connection=active
 if not connection and Run:IsStudio()then connection={url=legacy.Url,token=legacy.Token}end
 if not connection then return false end
 if connection.signalOnly then return true end -- skill settings stay inside this game
 local ok=pcall(function()assert(request(connection,'/ui/skills',settings).ok)end)
 return ok
end
Players.PlayerRemoving:Connect(function(player)if player.UserId==owner then M.Unpair(nil)end end)
race:SetAttribute('TikTokBridgeStatus',Run:IsStudio()and 'Studio local bridge'or 'Not paired — open gear to pair Game To Earn')
return M


