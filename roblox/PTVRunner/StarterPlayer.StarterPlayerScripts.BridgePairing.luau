local player=game.Players.LocalPlayer
local remote=game.ReplicatedStorage:WaitForChild('RaceSettings')
local screen=player.PlayerGui:WaitForChild('RaceSettingsUI')
local panel=screen:WaitForChild('SettingsPanel')
local locale=require(game.ReplicatedStorage:WaitForChild('RaceLocale'))
local button=Instance.new('TextButton');button.Name='BridgePairing';button.Text='Game To Earn ↗';button.Font=Enum.Font.GothamBold;button.TextSize=12;button.TextColor3=Color3.fromRGB(90,230,220);button.BackgroundTransparency=1;button.Position=UDim2.fromOffset(16,36);button.Size=UDim2.fromOffset(180,26);button.Parent=panel
local list=panel:WaitForChild('SkillList');list.Position=UDim2.fromOffset(10,68);list.Size=UDim2.new(1,-20,1,-188)
local modal=Instance.new('Frame');modal.Name='BridgePairingModal';modal.Size=UDim2.fromOffset(440,280);modal.AnchorPoint=Vector2.new(.5,.5);modal.Position=UDim2.fromScale(.5,.5);modal.BackgroundColor3=Color3.fromRGB(12,26,43);modal.Visible=false;modal.ZIndex=20;modal.Parent=screen
local corner=Instance.new('UICorner',modal);corner.CornerRadius=UDim.new(0,12)
local function label(class,name,text,y,height)
 local v=Instance.new(class);v.Name=name;v.Text=text;v.Position=UDim2.fromOffset(18,y);v.Size=UDim2.fromOffset(404,height);v.TextWrapped=true;v.BackgroundTransparency=1;v.TextColor3=Color3.new(1,1,1);v.Font=Enum.Font.GothamBold;v.TextSize=14;v.ZIndex=21;v.Parent=modal;return v
end
local title=label('TextLabel','Title','Pair Game To Earn',10,30)
local hint=label('TextLabel','Hint','Open PTVgame Launcher → Game To Earn → PTVRunner → Copy pairing code. Skill settings stay in this game. Private server owner only.',42,50);hint.TextSize=12
local code=label('TextBox','PairCode','',100,36);code.PlaceholderText='GTE1|…';code.ClearTextOnFocus=false;code.TextWrapped=false;code.TextSize=11;code.BackgroundTransparency=0;code.BackgroundColor3=Color3.fromRGB(30,50,71)
local status=label('TextLabel','Status','',143,45);status.TextSize=11
local pair=label('TextButton','Pair','Pair',197,32);pair.Size=UDim2.fromOffset(194,32);pair.BackgroundTransparency=0;pair.BackgroundColor3=Color3.fromRGB(31,166,152)
local unpair=label('TextButton','Unpair','Disconnect game',197,32);unpair.Position=UDim2.fromOffset(228,197);unpair.Size=UDim2.fromOffset(194,32);unpair.BackgroundTransparency=0;unpair.BackgroundColor3=Color3.fromRGB(43,58,78)
local close=label('TextButton','Close','Close',237,28)
local busy=false
local function invoke(action,data)
 if busy then return end;busy=true;status.Text='Connecting…'
 local ok,result=pcall(function()return remote:InvokeServer(action,data)end)
 status.Text=ok and (result.ok and ((result.status and result.status.status)or 'Done')or result.error)or 'Connection failed. Try again.'
 busy=false;return ok and result
end
button.Activated:Connect(function()modal.Visible=not modal.Visible;if modal.Visible then local r=invoke('BridgeStatus');if r and r.ok then status.Text=r.status.status end end end)
pair.Activated:Connect(function()local r=invoke('PairBridge',code.Text:match('^%s*(.-)%s*$'));if r and r.ok then code.Text=''end end)
unpair.Activated:Connect(function()invoke('UnpairBridge')end)
close.Activated:Connect(function()modal.Visible=false;code.Text=''end)
local translations={{title,'Pair Game To Earn'},{hint,'Open PTVgame Launcher → Game To Earn → PTVRunner → Copy pairing code. Skill settings stay in this game. Private server owner only.'},{pair,'Pair'},{unpair,'Disconnect game'},{close,'Close'}}
for _,entry in translations do locale.Bind(entry[1],entry[2])end


