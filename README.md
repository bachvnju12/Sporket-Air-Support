# Sprocket Drones 0.11.1

Mod drone trinh sát và kamikaze cho **Sprocket / BepInEx 6 IL2CPP**, tham khảo cách tích hợp của SAAPS.
Cần **SprocketKeybinds >=0.1.6 và <0.2.0**. Bản thử nghiệm: build/offline checks và bước nạp plugin
được kiểm tra riêng; hình ảnh, đổi phím và sát thương trong trận cần nghiệm thu trực tiếp.

## Cài đặt

Giải nén `dist/SprocketDrones-0.11.1.zip` vào thư mục chứa `Sprocket.exe`, giữ nguyên cả `BepInEx`
và `Sprocket_Data`, khi game đã đóng. Gói ZIP và installer chỉ nằm trên máy phát triển;
repo này chỉ chứa mã nguồn. Installer cục bộ sao lưu các tệp của mod, giữ config và mod khác.
DLL riêng lẻ không đủ: cần hai part JSON, localization và icon trong ZIP.

Mở **Vehicle Designer → Firepower → Drones**:

- **Scout drone launcher**: bệ mang scout kiểu ZALA/War Thunder, 12 kg.
- **Kamikaze drone launcher**: ống phóng drone nhỏ kiểu Switchblade, 8 kg; thay Strike UAV uplink.

Hộp đặt chìm vào thân xe: mặt nắp ở mặt lắp part, khoang sâu 0,44 m bên dưới. Trục +Z hướng phóng,
chừa khoảng trống trên/trước và phía bản lề trái. Mod không cắt lỗ giáp thân xe; cần bố trí khoang trên blueprint.
Hai loại đều phóng gần
module trên xe. Chỉ module đang gắn và còn hoạt động mới cho phép phóng/giữ liên lạc.
GUID và fileID của part cũ được giữ để blueprint cũ chuyển sang model kamikaze mới.

## Điều khiển

**Chuột lái bằng hướng ngắm**: di chuyển chuột chọn hướng, drone quay/ngẩng/chúi theo hướng đó.
Vòng tròn là hướng yêu cầu, dấu + là hướng mũi hiện tại. Con lăn thay đổi tốc độ.
Trong camera trinh sát, chuột điều khiển camera và con lăn zoom, drone tự giữ bay vòng.

| Phím mặc định | Chức năng |
|---|---|
| Alt + H | Phóng/chọn scout |
| Alt + K | Phóng/chọn kamikaze |
| Alt + J | Chuyển xe ↔ drone |
| Alt + G | Bay vòng ↔ lái chuột |
| Alt + C | Scout: camera trinh sát; kamikaze: camera FPV ↔ camera sau |
| Alt + X | Scout: khóa và đánh dấu điểm / bỏ khóa; kamikaze: bật/tắt đầu đạn |
| Alt + L | Tắt đầu đạn, cho drone quay về và chuyển về xe |

Đổi phím tại **Settings → Controls/keybinds → Mod keybinds → bachvnju → drone**.
Chỉ còn **8 mục: một Modifier và 7 phím chức năng**. SprocketKeybinds 0.1.6 chưa có chord/composite,
nên đổi từng thành phần: đặt Modifier thành Shift để dùng Shift+H/K/J…; có thể Clear slot Alt phụ.
Clear/Reset/Apply/Accept/Cancel và lưu phím do menu chung xử lý. HUD đọc phím đã đổi.
Các action ID mới của 0.3.0 dùng bộ mặc định mới; cấu hình cũ vẫn lưu để rollback nhưng không kích hoạt.
Không còn phím W/S/A/D/Q/E/Space của drone. Độ nhạy chuột ở `[Flight] MouseSensitivity`
trong `BepInEx/config/sprocket.drones.cfg`, mặc định 0.12 độ/pixel.

Scout: Alt+H → bay lên bằng chuột → Alt+C → ngắm → Alt+X để khóa/đánh dấu trong 30 giây.
Di chuyển chuột bỏ khóa. Alt+G quay lại lái chuột; Alt+J quay về xe, drone giữ bay vòng.

Kamikaze: Alt+K → mặc định **ARMED** → điều khiển và lao vào mục tiêu bằng chuột.
Alt+X tắt/bật đầu đạn trong chế độ lái tay; HUD báo rõ ARMED/SAFE. Va chạm khi ARMED tiêu hao chính drone;
không mang/bắn tên lửa. Chuyển về xe, bay vòng hoặc quay về tắt đầu đạn; cần Alt+X để bật lại khi lái tay.
Trúng đạn hoặc va chạm khi chưa bật đầu đạn chỉ làm mất drone.

## Cơ chế và giới hạn

Geometry gốc của mod: thân thuôn, cánh có biên dạng dày, đường ghép, bản lề, vít, camera và cánh quạt quay.
Model bay và part dùng chung geometry, 5 vùng material HDRP; icon render từ chính model part.
Scout có thân/cánh liền, winglet hướng xuống và camera dưới bụng theo ảnh War Thunder; kamikaze theo ảnh AeroVironment.
Ống phóng có lòng ống, vành miệng, đai kẹp, tay xách, dây nối và chân bắt vít. Không đóng gói ảnh/model bên thứ ba.

Bay cánh cố định đơn giản, luôn có tốc độ tối thiểu. Scout pin 15 phút, kamikaze 10 phút;
tự quay về khi còn dưới 60 giây hoặc cách xe hơn 2.500 m. Mất drone chờ 180 giây, thu hồi chờ 15 giây.
Thu hồi là cơ chế gameplay gần xe, chưa có hạ cánh thực. Khi lái drone, các input action của xe bị tắt:
lái/bắn, scope, động cơ, đèn, chữa cháy, bỏ xe, lệnh đội, đổi xe/vũ khí và hotkey combat của API chung.
Hotkey APS cũ và dispatch input của SprocketModAPI được chặn nếu các mod đó có mặt; APS tự động vẫn hoạt động.
Esc/pause, điều hướng menu và phím drone được giữ. Chỉ các action đang bật trước khi vào drone mới được khôi phục.

Kamikaze dùng **một projectile HEAT native** theo hướng đâm, đường kính 80 mm/2 kg và mức xuyên
480 mm RHA. Chỉnh `[Warhead] HeatPenetrationMm` trong `BepInEx/config/sprocket.drones.cfg` (50–1000).
Projectile register của game tự xử lý va chạm, giáp và spall; mod không dựng `DamageModelHit` và gọi
`HitDamageModel` trực tiếp từ C#. Đầu đạn HE riêng dùng vụ nổ native power 12 tại điểm chạm, thu hồi
proxy HE ngay sau đó. Contact HE tăng power từ 8 lên 12. Viên xuyên tồn tại tối đa 0,30 giây theo định nghĩa native, không tạo thêm luồng mỗi frame.
Không cần cài ShellSelector; không gọi các preset ERA/NERA riêng của mod đó.

Drone có HP riêng: **scout 100, kamikaze 80**, hiện bằng số và thanh nhỏ trên HUD.
Đạn native trúng thân/cánh theo tư thế máy bay gây sát thương tích lũy theo động năng, giới hạn 5–200 HP/phát.
Ví dụ cân bằng: đạn 9 g ở 800 m/s gây 24 HP; đạn pháo thường bắn hạ ngay. Đạn bay lệch hoặc bị tường/xe
chặn trước điểm trúng không gây damage cho drone. Bắn hạ drone thu hồi camera/input và không kích nổ đầu đạn.
Chưa mô phỏng mảnh nổ/flak lân cận, hỏng từng hệ thống hay xác máy bay rơi.

Cả **scout và kamikaze đều nằm thu cánh trong hộp chìm khi chờ**. Nhả khóa ở 0,18 s rồi mở hai cửa nhỏ
đối xứng sang hai bên 90° ở 0,80 s; bệ nâng bằng càng kéo hai tầng ở 1,80 s. Sàn bệ nâng cao 0,28 m
trên mặt lắp để ra khỏi hộp và cao hơn cửa. Chỉnh `[Launcher] RaisedHeight` từ 0,25–0,30 m nếu part đặt chìm sâu;
đặt mặt cửa ngang mặt giáp, vì mod chưa cắt lỗ giáp thân xe. Bốn ty chống cửa co/giãn theo bản lề;
ống kamikaze nâng lên 60 độ ở 2,45 s; nắp/kẹp mở hết ở 2,85 s.
**Scout: nâng hết → mở kẹp → bung đủ hai cánh → phóng ở 3,35 s.**
**Kamikaze: nâng hết → mở nắp → phóng ở 3,35 s → bung cánh sau khi ra khỏi ống.**
Cả hai chuyển sang bay ở 3,95 s. Drone dùng cùng một model từ lúc nằm trên bệ đến lúc bay;
không thay bằng model thu cánh tại thời điểm phóng. Cửa, thang và ống bám vị trí/góc xoay/tỷ lệ của part,
kể cả trong editor. Va chạm quét thể tích thân/cánh trên lớp projectile native, có kiểm tra overlap,
để bắt quệt hông, xích và đất; kamikaze ARMED kích nổ cả trong pha thoát bệ đã an toàn.
Đóng nắp ống/kẹp ở 4,35 s, hạ ống ngang ở 5,00 s, thu bệ ở 6,00 s, đóng khoang ở 6,65 s rồi gài khóa ở 6,85 s.
Nắp/bệ/cánh tăng và giảm tốc bằng đường cong có vận tốc/gia tốc bằng 0 ở hai đầu. Cánh quạt lên tốc dần,
camera chuyển từ xe sang drone trong 0,40 s sau khi thoát bệ. Tốc độ ra khỏi bệ được nối với tốc độ bay để tránh giật.
Animation theo thời gian game, giữ nguyên khi pause, dùng mesh con/pivot và không dựng lại mesh mỗi frame.
UAV trên bệ chuyển liền mạch sang trạng thái bay; lần phóng tiếp theo chờ cooldown. GUID/fileID blueprint giữ nguyên.
Collider giữ hình phần cứng đang thu trong animation; nắp mở và bệ nâng là hình ảnh động, chưa có collider chuyển động.
Collider dùng vỏ hộp đơn giản; chi tiết visual vẫn giữ nguyên. Mesh/material được dùng chung và làm nóng khi nạp part,
không dựng lại lúc phóng. Cơ cấu đứng yên ngừng cập nhật; drone trong hộp đóng được ẩn. Quay về editor/đổi scene
thu bệ và dọn drone; model clone từ cache được gắn lại cơ cấu để không giữ trạng thái mở từ battle.

Audit 0.6 loại bỏ hai lời gọi hit native thủ công khỏi cả va chạm HEAT và kiểm tra đạn trúng UAV;
thêm kiểm tra DLL để ngăn tái xuất hiện. SprocketKeybinds không bật lại các action được tắt khi lái
drone mỗi frame nữa; khi thoát lái, chỉ action trước đó bật được khôi phục.
Crash dump của phiên người dùng có Access Violation trong coreclr; chưa có stack đầy đủ để khẳng định
hàm gây crash. Startup/offline checks và thay đổi đường xử lý không thay thế nghiệm thu vụ nổ trong trận.

Chưa có thermal, radar, AI điều khiển drone, team/minimap scouting hoặc multiplayer replication.
Camera được khôi phục khi thoát điều khiển, pause, mất focus, đổi xe, rời trận hoặc unload.
HUD dùng Canvas/TextMeshPro native với thang hướng, tốc độ, độ cao AGL, link/pin, HP và reticle; lỗi HUD chỉ tắt HUD,
không xóa drone. Đã bỏ IMGUI gây lỗi `Method unstripping failed` trên game cài ở máy này.

## Build mã nguồn

Repo chỉ công bố `src/` và README; không chứa DLL game, bản build, ảnh, part JSON hay công cụ kiểm thử/cài đặt cục bộ.
Cần .NET 6 SDK, Sprocket với BepInEx 6 IL2CPP đã chạy một lần để tạo interop, và SprocketKeybinds 0.1.6.

```powershell
dotnet build src/SprocketDrones/SprocketDrones.csproj -c Release -p:SprocketGameRoot="D:\SteamLibrary\steamapps\common\Sprocket"
```

Đổi đường dẫn game khi cần. DLL nằm trong `src/SprocketDrones/bin/Release/net6.0/`.
DLL cần dùng cùng part/localization/icon từ bộ mod đầy đủ; repo mã nguồn không chứa các tài nguyên cài đặt này.
Các script build, kiểm thử và đóng gói vẫn giữ trên máy phát triển, không đưa lên Git.

Trước khi gỡ mod, tháo part drone khỏi xe và lưu lại, rồi di chuyển thư mục plugin và bốn tệp
`sdrone_` của mod ra ngoài game. Giữ bản sao blueprint và config khi rollback.

### Sửa va chạm xe và viên xuyên 0.11.0

Drone dò thêm hình học damage native của xe/xích bằng kết quả distance/normal dạng struct thuần,
không gọi API DamageModelHit boxed từng gây crash. Va chạm giáp tạo đúng một viên native 80 mm/2 kg
bắn theo hướng đâm từ cách mặt giáp 0,50 m; native register tự giải quyết trúng giáp và mảnh xuyên.
Đây là proxy cân bằng trong game: ngân sách 480 mm được hiệu chỉnh tại lần vào giáp đầu tiên,
không tăng lại ngân sách sau mỗi lớp. Bàn nâng mặc định 28 cm, khoảng chỉnh 25–30 cm.
Nạp lại model cùng part giữ pha cửa/bệ đang chạy; quay về editor vẫn thu lại.

### Sửa kích nổ 0.11.1

Nhấn lại phím chọn drone đang lái giữ nguyên trạng thái ARMED/SAFE. Dọn một projectile register khác
không xóa register kích nổ của drone. HE được kích nổ và thu hồi proxy trước khi tạo viên HEAT,
để tránh giữ tham chiếu vào pool qua một lần tạo projectile mới. Chuyển về xe, bay vòng hoặc quay về vẫn disarm;
Alt+X bật lại đầu đạn khi lượn ở chế độ lái tay.

## Giấy phép MIT

Giấy phép sau áp dụng cho mã nguồn và README của mod. Các thư viện game, BepInEx và SprocketKeybinds
không được phân phối trong repo và giữ giấy phép của chủ sở hữu.

```text
MIT License

Copyright (c) 2026 Sprocket Drones contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
