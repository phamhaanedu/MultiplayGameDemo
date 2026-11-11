# Dự Án Demo GAM302: Đấu Trường Sinh Tử 3D (Last Man Standing) — Photon Fusion 2 Shared Mode

## 1. Tổng Quan Dự Án (Project Overview)
- **Mục tiêu:** Demo toàn diện kiến thức môn học **GAM302** — Ứng dụng **Photon Fusion 2** phát triển game Multiplayer 3D hoạt động hoàn toàn ở chế độ **Shared Mode** trên **Unity 6 (URP)**.
- **Thể loại:** 3D Arena Battle Royale / Last Man Standing (Đấu trường sinh tồn 3D — Người chơi chiến đấu với nhau cho đến khi chỉ còn 1 người sống sót cuối cùng là người chiến thắng).
- **Chế độ mạng bắt buộc:** **Photon Fusion 2 — Shared Mode (`GameMode.Shared`)**.
- **Photon AppId:** `fbee3c4f-1f3b-42da-80b9-0e4d2b47f3a9`
- **Model Nhân vật:** Sử dụng prefab từ gói **Cartoon Capsule Pack Lite** (Unity Asset Store).

---

## 2. Kiến Trúc Mạng Cốt Lõi: Photon Fusion 2 — Shared Mode

### 2.1. Lý do & Đặc tả chế độ Shared Mode (`GameMode.Shared`)
- Fusion 2 hỗ trợ 3 mô hình chính: *Server Mode*, *Host Mode* và *Shared Mode*. Dự án này sử dụng duy nhất **Shared Mode**.
- **Cơ chế hoạt động của Shared Mode:**
  - Trạng thái trò chơi (Game State) được quản lý và phân phối trực tiếp bởi **Photon Cloud**, không phụ thuộc vào Dedicated Server đắt đỏ hay một máy Host cố định (tránh tình trạng chủ phòng thoát làm sập trận đấu).
  - **Quyền sở hữu đối tượng (`State Authority`) phân tán:** Khi một người chơi tham gia phòng và gọi `Runner.Spawn()` để tạo nhân vật của mình, **chính Client đó nắm giữ `StateAuthority`** đối với nhân vật của mình.
  - Đối với các đối tượng dùng chung trong phòng (như `MatchManager` quản lý thời gian trận đấu, trạng thái thắng/thua), quyền `StateAuthority` thuộc về `SharedModeMasterClient` (và tự động chuyển giao nếu Master Client rời phòng).

### 2.2. Thiết lập Kết nối & Khởi tạo Nhân vật
- **Cấu trúc mạng cơ bản:**
  - Thành phần `NetworkRunner` đóng vai trò trung tâm quản lý kết nối, phiên chơi (`Session`) và vòng lặp mô phỏng mạng.
  - Có thể kết hợp `Prototype Runner` / `Prototype Network Start` hoặc `NetworkManager` tùy chỉnh để truyền **Tên nhân vật (Player Name)** và **Region** trước khi gọi `StartGame(new StartGameArgs { GameMode = GameMode.Shared, ... })`.
- **Khởi tạo nhân vật (`Runner.Spawn`):**
  - Sử dụng `SimulationBehaviour` kết hợp các interface `IPlayerJoined`, `IPlayerLeft` để lắng nghe sự kiện người chơi vào/ra phòng.
  - Trong **Shared Mode**, khi `PlayerJoined(PlayerRef player)` kích hoạt và `player == Runner.LocalPlayer`, Client đó gọi `Runner.Spawn(playerPrefab, spawnPosition, spawnRotation, player)` (thay vì `GameObject.Instantiate`) để tạo nhân vật được đồng bộ trên toàn mạng.

---

## 3. Thiết Kế Môi Trường & Đấu Trường 3D (Arena Environment)
- **Mặt sàn (Floor):** Tạo từ đối tượng `Plane` 3D có gắn Collider và Material rõ ràng.
- **Đường biên (Arena Boundaries):** Sử dụng các `Empty GameObject` gắn `BoxCollider` bao quanh 4 cạnh sàn đấu để ngăn người chơi rơi khỏi đấu trường.
- **Chướng ngại vật (Obstacles):** Sử dụng các khối `Cube` 3D bố trí trong đấu trường làm vật cản che chắn tầm nhìn, cản đạn và tạo địa hình chiến thuật, phân tách bằng **Layer `Environment` / `Obstacle`** để tối ưu kiểm tra va chạm.

---

## 4. Hệ Thống Nhân Vật, Chuyển Động & Chiến Đấu (Shared Mode)

### 4.1. Đồng bộ hóa Chuyển động trong `FixedUpdateNetwork()`
- Mọi logic di chuyển, xoay hướng và kỹ năng của nhân vật đều được thực thi trong phương thức `FixedUpdateNetwork()` để chạy theo **Tick mạng** của Fusion, cách ly hoàn toàn khỏi sự chênh lệch tốc độ khung hình (FPS) giữa các máy Client.
- Kiểm tra quyền điều khiển thông qua `if (!Object.HasStateAuthority) return;` (chuẩn **Shared Mode**).
- Kết hợp `NetworkTransform` (hoặc `NetworkTRSP` / `NetworkRigidbody3D`) để tự động nội suy (Interpolation) vị trí và góc xoay mượt mà trên màn hình của những người chơi khác.

### 4.2. Bộ kỹ năng điều khiển nhân vật & Kiến trúc Dữ liệu / Phím bấm
1. **Di chuyển 360 độ:** Di chuyển linh hoạt theo 8 hướng / 360 độ bằng cụm phím `WASD`.
2. **Xoay góc mặt theo hướng chuột:**
   - Nhân vật xoay hướng nhìn theo thao tác di chuột của người chơi (hỗ trợ đồng bộ góc xoay thông qua `NetworkTransform`).
3. **Lướt về phía trước (Forward Dash — Phím mặc định: `2`):**
   - Bấm phím số `2` để nhân vật phóng nhanh về phía trước một khoảng ngắn.
   - Sử dụng `[Networked] TickTimer` để quản lý thời gian lướt và thời gian hồi chiêu (Cooldown) đồng bộ theo Tick mạng.
4. **Lăn sang trái / phải (Side Dodge Roll — Phím mặc định: `Q` / `E`):**
   - Bấm phím `Q` (lăn trái) hoặc `E` (lăn phải) để nhân vật lăn né đòn nhanh sang hai bên, kèm hiệu ứng nghiêng/đàn hồi thân người (DOTween / Animator).
5. **Đánh cận chiến đa hướng (Melee Attack — 2 chiêu thức / 3 hướng đánh):**
   - **Nhát chém dọc (`Vertical Slash` — Mặc định: `Left Click`):** Quét vùng va chạm hình quạt dọc thẳng từ trên xuống ngay phía trước mặt nhân vật.
   - **Nhát chém ngang xoay người $90^\circ$ (`Horizontal Turning Slash` — Mặc định: Phím `1` chém trái, Phím `3` chém phải):** Quét vùng va chạm hình quạt ngang góc $90^\circ$ từ thẳng trước mặt sang trái (`1`) hoặc sang phải (`3`); đồng thời **xoay mượt đồng bộ cả Nhân vật lẫn Camera (`FPS`/`TPS`) $90^\circ$ theo hướng tương ứng trong `~0.15s`**.
   - Đồng bộ hiệu ứng vung đòn và âm thanh 3D qua `[Rpc]`.
6. **Đánh xa (Ranged Attack — Mặc định: `Right Click`):**
   - Bấm chuột phải (`Right Click`) để bắn đạn tầm xa về phía trước theo hướng ngắm của nhân vật.
   - Ứng dụng `Runner.Spawn()` kết hợp **Object Pooling (`INetworkObjectProvider`)** cho viên đạn và kiểm tra đường đạn có bù trễ mạng.
7. **Cụm Button mềm trên màn hình (Mobile-Ready On-Screen Action Buttons):**
   - Tất cả các chiêu thức (`Left Click`, `Right Click`, `1`, `2`, `3`, `Q`, `E`) đều có **Button mềm hiển thị ở góc dưới bên phải màn hình** giống các game trên Mobile (hiển thị icon, Cooldown và phím tắt), thiết kế sẵn sàng cho việc chuyển đổi sang Mobile sau này.
8. **Hệ thống Gán phím linh hoạt (`keybinding.json` + In-Game Rebind UI):**
   - Ngay đầu game tại **Loading Scene**, gọi đọc file `keybinding.json` để nạp cấu hình phím bấm.
   - Có **bảng UI gán phím (Rebind UI)** trực tiếp trong game: khi người chơi đổi phím và bấm **Lưu (Save)**, kết quả mới được áp dụng ngay và lưu lại vào `keybinding.json`.
9. **Định nghĩa thông số qua `ScriptableObject` (`CharacterCombatConfigSO`):**
   - Toàn bộ thông số nhân vật (Máu tối đa, tốc độ di chuyển, tốc độ lướt, tốc độ lăn né, sát thương từng loại đòn đánh dọc/ngang/tầm xa, độ xa & góc quạt đánh cận chiến, độ xa & tốc độ đạn đánh xa, Cooldown từng chiêu) đều được định nghĩa tập trung thông qua một file `ScriptableObject`.

---

## 5. Hệ Thống Camera (FPS $\leftrightarrow$ TPS) & Ghost Mode (Spectator)

### 5.1. Camera Góc nhìn thứ nhất (FPS) & Góc nhìn thứ ba (TPS)
- Ứng dụng **Cinemachine Virtual Camera** (`CinemachineCamera`).
- Khi nhân vật được Spawn, kiểm tra `Object.HasStateAuthority` để gắn Camera chỉ theo dõi đúng nhân vật của máy người chơi đó.
- **Chuyển đổi góc nhìn bằng phím bấm (ví dụ: phím `V`):**
  - **Chế độ FPS (First-Person):** Camera đặt tại tầm mắt của nhân vật.
  - **Chế độ TPS (Third-Person):** Camera lùi ra phía sau và cao hơn vai nhân vật để quan sát toàn cảnh xung quanh.
  - Người chơi có thể bấm phím chuyển đổi qua lại giữa **FPS $\leftrightarrow$ TPS** bất kỳ lúc nào khi đang còn sống.

### 5.2. Chế độ Hồn ma / Quan sát khi chết (Ghost Mode / Spectator)
- Khi máu nhân vật về `0` (`CurrentHealth <= 0`), nhân vật chuyển sang trạng thái `[Networked] NetworkBool IsDead = true`.
- Nhân vật bị hạ gục sẽ ngừng tham gia chiến đấu và kích hoạt **Ghost Mode**:
  - Camera Cinemachine của người chơi vừa chết tự động chuyển sang theo dõi (Follow/LookAt) **một người chơi khác còn sống** trên đấu trường.
  - Trên giao diện hiển thị nút bấm (hoặc phím tắt `Next` / `Previous`) cho phép người chơi **chuyển đổi mục tiêu quan sát sang những người chơi còn sống khác**.
- **Điều kiện Chiến thắng (Last Man Standing):**
  - Khi số lượng người chơi còn sống (`AliveCount`) giảm xuống chỉ còn **1 người cuối cùng** (khi trận đấu có từ 2 người trở lên), hệ thống công bố **Người Chiến Thắng (Winner)**, hiển thị bảng kết quả và lưu thành tích lên PlayFab.

---

## 6. Thuộc Tính Mạng (`[Networked]`), HUD & Giao Diện Người Chơi

### 6.1. Đặt tên trước khi vào phòng & Giao diện trên đầu nhân vật (Overhead UI)
- **Trước khi Join Shared Mode:** Người chơi nhập **Tên nhân vật** tại màn hình Lobby.
- **Sau khi Spawn vào trận:**
  - Tên nhân vật được lưu vào thuộc tính mạng `[Networked] public NetworkString<_16> PlayerName { get; set; }`.
  - **Trên đầu mỗi nhân vật (World-Space Canvas):**
    - Hiển thị **Tên nhân vật**.
    - Hiển thị **Thanh máu (Health Bar)** kèm **Số máu còn lại** (Máu khởi điểm: `100 / 100`).
    - Áp dụng kỹ thuật **Billboard UI** (`LookAt` hoặc `Quaternion.LookRotation`) để thanh tên và máu trên đầu luôn xoay hướng trực diện về phía Camera của người nhìn.

### 6.2. Đồng bộ Chỉ số Máu & Cập nhật UI tức thời
- Khai báo chỉ số máu bằng thuộc tính `[Networked]` kèm thuộc tính `[OnChangedRender(nameof(OnHealthChanged))]`:
  - Ngay khi giá trị máu thay đổi qua mạng, callback `OnHealthChanged` tự động cập nhật thanh máu trên đầu nhân vật và thanh máu trên HUD chính mà không cần kiểm tra liên tục trong `Update()`.
- Đối với các thông tin trạng thái phức tạp hơn của người chơi/trận đấu, đóng gói bằng `struct` thực thi `INetworkStruct`.

### 6.3. Thiết lập HUD chính (Screen-Space UI)
- **Thanh máu bản thân (Local Player Health Bar):** Hiển thị rõ ràng lượng máu hiện tại (`100/100`).
- **Bảng điểm & Sinh tồn (Scoreboard / Alive Counter):** Hiển thị số người chơi còn sống, số mạng hạ gục (Kills) và danh sách trạng thái người chơi trong phòng.
- **Đồng hồ trận đấu (Match Timer):** Đồng bộ bằng `[Networked] TickTimer` để mọi máy Client đều hiển thị đúng thời gian trận đấu như nhau.

---

## 7. Remote Procedure Calls (RPCs), Sát Thương, Vật Lý & Bù Trễ Mạng

### 7.1. Cơ chế RPCs trong Shared Mode
- Sử dụng thuộc tính `[Rpc]` với cấu hình `RpcSources` và `RpcTargets` phù hợp với **Shared Mode**:
  - **Gây sát thương (`Rpc_TakeDamage`):** Trong Shared Mode, chỉ `StateAuthority` của mục tiêu mới có quyền trừ máu của chính mình. Khi Người chơi A đánh trúng Người chơi B, máy A gọi `[Rpc(RpcSources.All, RpcTargets.StateAuthority)]` lên nhân vật B để B tự trừ `CurrentHealth` và phản hồi kết quả hạ gục.
  - **Hiệu ứng & Kỹ năng (`Rpc_PlayAttackEffect`):** Sử dụng `[Rpc(RpcSources.StateAuthority, RpcTargets.All)]` để phát hiệu ứng đòn đánh, tia đạn và âm thanh cho tất cả người chơi cùng thấy.

### 7.2. Đồng bộ Âm thanh 3D (3D Spatial Audio)
- Kết hợp lệnh gọi `RPC` với thành phần `AudioSource` gắn trên nhân vật/vũ khí.
- Thiết lập `Spatial Blend = 1.0` (3D Audio) cùng khoảng cách `Min/Max Distance` hợp lý để âm thanh bước chân, tiếng chém cận chiến, tiếng bắn xa và tiếng trúng đòn thay đổi cường độ chân thực theo khoảng cách và hướng giữa nguồn phát và người nghe.

### 7.3. Đồng bộ Vật lý, Xử lý Va chạm & Bù trễ mạng (Lag Compensation)
- **Đồng bộ vật lý:** Quản lý trạng thái `Rigidbody` / vận tốc (`Velocity`) dưới sự kiểm soát của `StateAuthority`, đảm bảo tương tác vật lý nhất quán khi nhân vật di chuyển, lướt hoặc va chạm chướng ngại vật.
- **Bù trễ mạng (Lag Compensation):**
  - Thiết lập `HitboxRoot` và `Hitbox` trên prefab nhân vật kết hợp API `Runner.LagCompensation` (Raycast / OverlapSphere) và cơ chế kiểm tra va chạm chuẩn xác để xác định đường đạn và đòn đánh hợp lệ ngay cả khi có độ trễ mạng giữa các Client.

---

## 8. Hệ Thống Chat & Tích Hợp Đám Mây PlayFab

### 8.1. Hệ thống Chat trong Game (In-Game Chat)
- Hỗ trợ giao tiếp trực tiếp giữa những người chơi trong đấu trường:
  - **Kênh Chat công cộng (Public Chat):** Gửi tin nhắn cho toàn bộ người chơi trong phòng thông qua Fusion RPCs (hoặc Photon Chat SDK).
  - **Tin nhắn riêng (Private Chat / Whisper):** Gửi tin nhắn riêng tư tới một người chơi cụ thể theo tên nhân vật.
  - Giao diện khung Chat tối ưu bằng `CanvasGroup` (tự động làm mờ khi không thao tác, bật sáng khi nhấn `Enter` để gõ phím, khóa điều khiển nhân vật trong lúc đang nhập chat).

### 8.2. Kết nối Backend đám mây PlayFab
- Tích hợp **PlayFab SDK** vào dự án Unity:
  - **Quản lý định danh & Lưu trữ dữ liệu (Player Data / Title Data):** Đăng nhập người chơi (gắn với Tên hiển thị), lưu trữ thông tin cá nhân (tổng số trận thắng, tổng số mạng hạ gục, cấu hình từ Title Data).
  - **Bảng xếp hạng (Leaderboards):** Tự động gửi điểm chiến thắng (`Wins` / `Kills`) lên PlayFab Statistic khi kết thúc ván đấu và hiển thị Bảng xếp hạng Top người chơi ngay trong game.

---

## 9. Định Hướng Kỹ Thuật Tối Ưu Hóa Hiệu Suất (Optimization)
1. **Chọn Region cân bằng tải (Region Selection):** Cho phép chọn cụm máy chủ Photon Cloud (ví dụ: `asia`, `jp`, `sg`, `us`) hoặc tự động kết nối Region có Ping thấp nhất.
2. **Object Pooling (`INetworkObjectProvider`):** Tái sử dụng các đối tượng sinh/hủy liên tục trên mạng như đạn bắn xa, hiệu ứng va chạm (VFX) thay vì cấp phát bộ nhớ mới liên tục.
3. **Tối ưu hóa Layer Va chạm (Physics Layer Matrix):** Phân tách rõ các Layer (`Player`, `Projectile`, `Obstacle`, `Boundary`) và tắt các cặp kiểm tra va chạm không cần thiết trong Physics Settings.
4. **Giảm tải UI (`CanvasGroup` & Event-driven UI):**
   - Dùng `CanvasGroup` (thay vì `SetActive` bật/tắt cả cây UI lớn) để ẩn/hiện các bảng HUD, Chat, Leaderboard mà không gâyRebuild Canvas tốn CPU.
   - Chỉ cập nhật UI khi có sự kiện thay đổi dữ liệu (`[OnChangedRender]`, C# Events) thay vì cập nhật mỗi khung hình trong `Update()`.
5. **Điều chỉnh Tick Rate & Network Send Rate:** Cấu hình Tick Rate hợp lý trong `NetworkProjectConfig` cho thể loại đấu trường hành động Shared Mode để cân bằng giữa độ nhạy điều khiển và băng thông mạng.
