# macro — MÔ TẢ TÍNH NĂNG KẾ HOẠCH PHÁT TRIỂN

> Tài liệu mô tả các nhóm chức năng định triển khai tiếp cho phần mềm
> **macro** (trình tạo macro/automation không cần viết code, C#/WPF).
> Mỗi nhóm là một **thẻ chuyên mục** trên Action Library, giúp người dùng
> tìm thao tác theo mục đích sử dụng thay vì phải nhớ tên action.

---

## NHÓM 1: CƠ BẢN — Tự động hóa chuột, bàn phím, ứng dụng

**Mục đích chung:** tập hợp những thao tác nền tảng mà hầu hết macro đều
cần: điều khiển chuột/bàn phím, tương tác với cửa sổ ứng dụng, và chạy
tập lệnh con. Đây là nhóm mặc định hiển thị đầu tiên trong Action Library.

### 1.1. Nhấn chuột (Click / Double click / Right click)
- Người dùng chọn tọa độ **X–1, Y–1** (tọa độ chính) và tùy chọn
  **X–2, Y–2** (tọa độ thứ hai — cho thao tác cần nhấn ở 2 điểm, ví dụ
  giữ chuột rồi click nơi khác).
- Tham số kèm theo: **Delay trước/khi nhấn (ms)**, khoảng **ngẫu nhiên
  hóa** tọa độ X±, Y± (tránh phát hiện là bot vì mỗi lần click lệch nhẹ
  vài pixel), nút chuột (trái/giữa/phải), số lần nhấn.
- Cách hoạt động: action gửi sự kiện chuột thật xuống hệ điều hành qua
  API SendInput; tọa độ có thể nhập tay hoặc bấm **Chọn theo kiểu…**
  rồi click trực tiếp lên màn hình để lấy.

### 1.2. Hiển thị hoạt động đang chạy
- Tùy chọn trình bày khi macro đang chạy: **nổi dung hiển thị** (chữ
  thông báo nổi trên màn hình), **màu** nền thông báo, và **skin/hình**
  đại diện.
- Mục đích: khi macro chạy ẩn ở nền, người dùng vẫn biết tool đang làm
  bước nào, không bấm nhầm vào ứng dụng đang được điều khiển.

### 1.3. Văn bản và bàn phím
- **Nhập văn bản** (gõ nội dung vào cửa sổ đang mở) với các chế độ:
  nhập nhanh (paste tốc độ cao) hoặc **nhập từng ký tự**; **giãn cách
  nhập (ms)** giữa các ký tự; tùy chọn **ngẫu nhiên** giãn cách để giống
  người gõ thật.
- **Nhập/viết Enter**: số lần Enter, delay giữa các lần (ms).
- Tất cả tham số có nút **Thử** để chạy thử riêng lẻ action đó mà không
  cần chạy toàn bộ workflow.

### 1.4. Kiểm tra kết nối Internet
- Action tiện ích **ping/check mạng** trước khi chạy các bước cần
  mạng (đăng nhập game, tải trang web…).
- Kết quả trả về biến `kết nối` (có mạng / mất mạng) để các bước sau
  rẽ nhánh theo điều kiện.

### 1.5. Phóng to, thu nhỏ (Zoom)
- **Phóng to / Thu nhỏ / Về mặc định** cho cửa sổ hoặc trang web đang
  làm việc — hữu ích khi macro cần căn lại giao diện trước khi tìm ảnh
  (vì layout khác nhau theo độ zoom).

### 1.6. Thao tác với ứng dụng
- **Tắt ứng dụng đúng tiêu đề**: đóng/kill tiến trình theo tên cửa sổ,
  có nút **Lấy danh sách** liệt kê ứng dụng đang mở để chọn nhanh thay
  vì gõ tay.
- **Khởi động ứng dụng / đường dẫn file (.apk, .exe…)**: mở app, cài
  đặt giả lập Android (file .apk) hoặc chương trình bất kỳ.
- **Đóng / Gỡ cài đặt / Cài đặt**: thao tác quản lý ứng dụng từ trong
  macro, phục vụ kịch bản kiểu "reset app rồi đăng nhập lại".
- **Thêm lệnh**: mở rộng thao tác ứng dụng bằng câu lệnh tùy chỉnh.

### 1.7. Chạy tập lệnh con (Sub-script)
- Gọi một **workflow con** khác (đường dẫn tập lệnh) ngay giữa workflow
  chính — giúp tách kịch bản dài thành nhiều module nhỏ, tái sử dụng.
- **Chạy ngẫu nhiên tập lệnh con**: liệt kê nhiều tập lệnh (a, b, c…),
  tool chọn ngẫu nhiên một cái để chạy — dùng cho hành vi "giống người"
  hoặc test đa tình huống.
- **Số lần chạy lại** và tùy chọn **không chạy lại lệnh này**: kiểm soát
  việc lặp/quay lại tập lệnh con, tránh vòng lặp vô hạn.

---

## NHÓM 2: VĂN BẢN — Quản lý dữ liệu dạng text và biến

**Mục đích chung:** biến macro thành tool xử lý dữ liệu, không chỉ bấm
già: đọc danh sách từ file text, lấy từng dòng để nhập, lưu kết quả ra
file/biến. Ứng dụng điển hình: nhập liệu hàng loạt từ danh sách tài khoản,
comment tự động từ kho caption.

### 2.1. Lấy tệp / Lấy trong thư mục text của tool
- Chọn **tệp nguồn** (file .txt) hoặc dùng **thư mục text riêng của
  tool** để quản lý tập trung danh sách dữ liệu.
- Tùy chọn **thêm tên giao lệ vào đầu tên file** (tiền tố) khi cần
  phân biệt nhiều nguồn dữ liệu.

### 2.2. Lấy nội dung theo dòng
- **Lấy dòng thứ N**: lấy chính xác dòng cần thiết.
- **Lấy ngẫu nhiên**: lấy một dòng bất kỳ (ví dụ caption ngẫu nhiên).
- **Lấy hết**: đọc toàn bộ nội dung file.
- **Lấy xong xóa**: sau khi lấy, xóa dòng đã dùng theo cách người dùng
  chọn (xóa dòng / giữ kết quả…) — đảm bảo **không bao giờ dùng trùng
  dữ liệu** giữa các lần chạy; phù hợp kịch bản nhập hàng loạt từng
  tài khoản một.

### 2.3. Nhập từ văn bản lấy từ biến
- Nhập nội dung đã lưu trong **biến** (kết quả của action trước, hoặc
  dòng lấy từ file) ra màn hình, thay vì nhập text cố định.
- Tùy chọn **nhập theo bộ phự chữ / nhập phần tử / ngăn cách bởi**:
  tách chuỗi theo ký tự phân cách (dấu phẩy, xuống dòng…) rồi nhập từng
  phần — ví dụ `user,pass` tách thành 2 ô nhập khác nhau.
- Điều khiển tốc độ: nhập nhanh / nhập từng ký tự + giãn cách ms +
  ngẫu nhiên.

### 2.4. Lưu văn bản
- **Lưu kết quả xử lý vào biến** với **tên biến** tự đặt và tùy chọn
  **thời gian lưu** (biến sống bao lâu, xóa khi nào).
- **Lưu toàn bộ văn bản vào biến** hoặc **lưu phần tử** (một phần được
  tách theo **ngăn cách bởi**).
- Mục đích: tạo "bộ nhớ" giữa các bước — action trước ghi, action sau
  đọc (vd: lưu mã OTP vừa nhận rồi dán sang ô khác).

### 2.5. Lưu vào file / Lưu vào biến / Ghi đè
- **Lưu vào file** với tên file tùy chọn, hoặc lưu vào **thư mục text
  của tool**; chế độ **ghi đè** (thay toàn bộ) hoặc ghi nối tiếp.
- Là bước cuối của chu trình: đọc dữ liệu → xử lý → **xuất kết quả ra
  file** để đối chiếu/log.

---

## NHÓM 3: TÌM HÌNH ẢNH — Tìm kiếm nâng cao có điều kiện

**Mục đích chung:** nâng cấp action "Find Image" hiện tại thành bộ tìm
kiếm hoàn chỉnh: tìm nhiều lần, giới hạn vùng, lưu tọa độ vào biến,
và **rẽ nhánh theo kết quả** (thấy → làm A, không thấy → làm B). Đây
là trái tim của automation kiểu game/web.

### 3.1. Tham số tìm kiếm
- **Tìm N lần**, **gian cách (ms)** giữa các lần — chịu khó tìm lại cho
  tới khi thấy hoặc hết số lần, thay vì chỉ tìm 1 lần.
- **Hình mới**: chọn ảnh mẫu từ file hoặc chụp nhanh ngay trong tool.
- **Độ chính xác (%)**: mức khớp tối thiểu giữa ảnh mẫu và màn hình —
  ảnh layout hay đổi thì hạ xuống, cần chính xác tuyệt đối thì nâng lên.
- **Tìm trong vùng**: khoanh vùng X/Y/W/H chỉ tìm trong khung đó —
  tìm nhanh hơn nhiều và tránh bắt nhầm hình giống ở nơi khác trên màn hình.

### 3.2. Lưu tọa độ vào biến
- **Tọa độ đầu tiên tìm được sẽ lưu vào biến**: `X →` và `Y →` ghi vào
  biến người dùng đặt tên, để các action sau dùng lại (click vào chỗ
  vừa tìm, tính toán tọa độ lệch…).
- **Lưu tất cả tọa độ vào chuỗi**: khi màn hình có nhiều kết quả giống
  nhau (vd 10 nút), lưu hết danh sách tọa độ để lần lượt xử lý từng cái.

### 3.3. Điều kiện đúng / sai (If found / If not found)
- **Nếu đúng**: nhóm lệnh chạy khi **tìm thấy** hình.
- **Nếu sai**: nhóm lệnh chạy khi **không tìm thấy** — ví dụ thấy nút
  "Quảng cáo" thì bấm tắt, không thấy thì tiếp tục công việc chính.
- Đây chính là hành động theo điều kiện (If/Else) gắn với kết quả tìm
  ảnh, giúp macro tự xử lý được nhiều tình huống.

### 3.4. Hành động sau khi tìm thấy
- **Đợi … ms** trước khi thao tác (cho hiệu ứng animation chạy xong).
- **Nhấp vào hình**: click luôn vào giữa ảnh vừa tìm được.
- **Cộng thêm vào tọa độ trước khi thực hiện**: X+, Y+ (dịch điểm click
  so với ảnh tìm được, ví dụ bấm vào nút nằm cạnh ảnh) + tùy chọn
  **ngẫu nhiên** vài pixel để tự nhiên hơn.
- **Chạy tập lệnh con**: gọi module xử lý riêng cho trường hợp này.

### 3.5. Điều khiển luồng sau tìm kiếm (Threshold lệnh)
- **Dừng hết lệnh**: tìm thấy/thất bại thì kết thúc toàn bộ workflow.
- **Di chuyển đến**: nhảy tới một dòng lệnh khác trong kịch bản (goto)
  hoặc **quay lại lệnh cũ**.
- **Di chuyển đến dòng lệnh thứ N** / **di chuyển đến vị trí đánh dấu**:
  điều hướng kịch bản theo nhãn (label) — tạo vòng "lặp cho tới khi
  hình xuất hiện" mà không cần khối Loop bao ngoài.
- **Chỉ chạy với trạng thái**: giới hạn hành vi theo trạng thái hiện
  tại của tool (đang chạy/thành công/thất bại).

---

## THỨ TỰ ƯU TIÊN TRIỂN KHAI (đề xuất)

| Ưu tiên | Nhóm | Lý do |
|---|---|---|
| 1 | Tìm hình ảnh (3.1–3.4) | Nền tảng đã có (Find Image + Click Image); chỉ cần thêm biến tọa độ và If đúng/sai là mở khóa 80% kịch bản thực tế |
| 2 | Cơ bản (1.1, 1.3, 1.6, 1.7) | Bổ sung tọa độ thứ 2, ngẫu nhiên hóa, tắt app theo tiêu đề, chạy tập lệnh con — đều dựa trên engine hiện có |
| 3 | Văn bản (2.2, 2.3, 2.4) | Cần xây hệ thống **biến** trước (đã chừa sẵn `ExecutionContext.Variables`), sau đó đọc/ghi file text là bổ sung action mới thuần túy |
| 4 | Còn lại (1.2, 1.4, 1.5, 3.5, 2.5) | Tính năng tiện ích, làm khi các nhóm chính ổn định |

> **Ghi chú kỹ thuật:** kiến trúc hiện tại đã chuẩn bị sẵn cho cả 3 nhóm:
> (1) hệ thống biến tồn tại trong `ExecutionContext.Variables`;
> (2) registry cho phép thêm action mới không phải sửa UI;
> (3) engine đã hỗ trợ điều kiện dừng/nhảy qua `BreakLoop` và step
> enable/disable — nền móng cho If/Else và goto theo nhãn.
