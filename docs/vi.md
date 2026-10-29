# Mod Manager — mod, game và tài liệu của chúng

Mod Manager là màn hình **Mod và bộ sưu tập** của menu chính. Dùng nó để xem mod nào đã cài, đọc xem mỗi mod làm gì, cài hoặc cập nhật mod từ danh mục trực tuyến, tắt mod, và soi vào các game làm sẵn để thấy chúng được ghép từ những mod nào.

Nó thuộc lớp vỏ luôn được tải cùng menu, nên hoạt động ngay cả trước khi bạn bắt đầu một game.

## Mở nó

Trong menu chính chọn **Mod và bộ sưu tập**. Màn hình có hai thẻ:

- **Mod** — mọi mod mà game biết: những mod đã cài và những mod danh mục đề nghị.
- **Bộ sưu tập** — các game làm sẵn, và sơ đồ mod cấu thành từng game.

**← Menu** quay về menu chính. Nút **?** ở góc trên phải, hoặc **F1**, bật gợi ý theo ngữ cảnh: đưa chuột lên một điều khiển để xem nó làm gì, nhấp để ghim gợi ý, **Esc** để thoát.

## Duyệt mod

![Thẻ Mod: bộ lọc bên trái, lưới thẻ mod có ảnh bìa, ô tìm kiếm đang mở gợi ý, sắp xếp và nút Refresh catalog.](assets/mods-list.jpg)

Mỗi thẻ cho thấy ảnh bìa của mod, tiêu đề, tác giả, danh mục mà nó đến từ, bao nhiêu game dùng nó (**trong 4 bộ sưu tập**), trạng thái và phiên bản, cùng một công tắc bật/tắt.

- **Tìm mod...** lọc theo tiêu đề, tác giả hoặc định danh.
- **Theo tên / Theo tác giả / Theo trạng thái** sắp xếp danh sách.
- **Bộ lọc** bên trái thu hẹp danh sách theo **Trạng thái** (Chưa cài, Đã cài, Có bản cập nhật, Có phiên bản cũ hơn, Đã sửa cục bộ, Đã tắt) và theo **Thể loại** như Cốt truyện, Lối chơi, Hình ảnh, Âm thanh, Thư viện, Engine và Công cụ. **Đặt lại** xóa bộ lọc và tìm kiếm.
- **Làm mới danh mục** tải về danh sách mod đang xuất bản hiện thời.

*Đã sửa cục bộ* nghĩa là các tệp đã cài khác với bản sao cùng phiên bản trên danh mục — ví dụ, một mod bạn đang tự sửa.

## Đọc về một mod

Nhấp vào một thẻ để mở trang của nó: ảnh bìa, bộ chọn phiên bản, các thao tác, mô tả ngắn từ manifest của mod và, khi mod có cung cấp, **Tài liệu** của nó — một cẩm nang đầy đủ như bản bạn đang đọc.

![Một trang mod: ảnh bìa và phiên bản của Audio Engine, các nút Reinstall và Disable, "in 4 collections", phần mô tả, và khối Documentation với bộ chọn ngôn ngữ.](assets/mod-page.jpg)

Tài liệu là cẩm nang của phiên bản **đã cài**. Nếu bạn chọn phiên bản khác trong bộ chọn, một dòng nhắc sẽ cho biết hướng dẫn bên dưới vẫn thuộc về bản đã cài.

**Ngôn ngữ tài liệu.** Bộ chọn bên phải tiêu đề Tài liệu khởi đầu ở **Tự động · ngôn ngữ giao diện**: cẩm nang theo ngôn ngữ giao diện của game. Nếu mod không có hướng dẫn bằng ngôn ngữ đó, trình quản lý hiển thị hướng dẫn mặc định của mod, rồi tiếng Anh, rồi tài liệu đầu tiên tìm được; dòng phía trên cẩm nang ghi ngôn ngữ thực tế đang hiện (ví dụ `EN`). Bạn cũng có thể tự chọn ngôn ngữ trong bộ chọn. Mod không có hướng dẫn nào sẽ tiếp tục hiển thị mô tả trong manifest.

Cho tác giả mod: đặt cẩm nang vào `docs/en.md` (bản dịch ở `docs/<ngôn ngữ>.md`, hoặc `docs/default.md`) và ảnh cạnh đó, ví dụ `docs/assets/screen.png`, được dẫn như `assets/screen.png`. Trình xem hiểu tiêu đề, đoạn văn, danh sách, trích dẫn, khối mã, liên kết và ảnh PNG/JPEG/WebP; nó không chạy HTML, bảng hay hoạt ảnh.

## Cài, cập nhật, tắt, xóa

Các nút trên trang mod và công tắc trên thẻ:

- **Cài đặt / Cập nhật / Hạ phiên bản / Cài lại** tải phiên bản đã chọn từ danh mục về thư mục người dùng của bạn (`user://mods/`).
- **Tắt / Bật** (hoặc công tắc trên thẻ) quyết định mod có được nạp hay không. Mod bị tắt vẫn giữ tệp và dữ liệu trong các bản lưu của bạn.
- **Xóa** gỡ một mod đã tải về sau khi xác nhận.

Sau bất kỳ thao tác nào ở trên, một dải thông báo xuất hiện: **Thay đổi có hiệu lực sau khi khởi động lại game**. Chính Mod Manager không thể bị xóa hay tắt từ màn hình của nó.

**Đã cài không có nghĩa là đang được dùng.** Một game nạp các mod được nêu trong bộ sưu tập của nó, cộng lớp vỏ menu. Cài mod chỉ làm mod khả dụng; nó xuất hiện trong game khi bộ sưu tập của game đó bao gồm nó.

## Game và bộ sưu tập

Thẻ **Bộ sưu tập** đầu tiên hiển thị các game làm sẵn. Một game là một *bộ sưu tập*: một bộ mod có tên cùng địa điểm bắt đầu.

![Phòng trưng bày game: các thẻ Cthangover, My Novel và RPG Plus, đều "Ready to play" với Mods 32/32, 11/11 và 36/36, và nút Edit scheme.](assets/games-gallery.jpg)

Thẻ cho bạn biết mọi thứ game cần đã được cài chưa (**Sẵn sàng để chơi**, hoặc **Thiếu N** / **Cài (N)** / **Bật (N)**). Một nút cài hoặc bật phần còn thiếu. **Sửa sơ đồ** mở cấu trúc của game; **＋ Tự làm game của bạn** bắt đầu một bộ sưu tập mới.

### Sơ đồ của một game

![Sơ đồ của My Novel: các khối Story, Assets và 2D engine cùng mod của chúng, mũi tên từ mỗi phụ thuộc tới mod cần nó, và thanh thuộc tính cho thấy yêu cầu phiên bản của My Novel.](assets/collection-scheme.jpg)

Sơ đồ cho thấy các *cơ chế* của game (những khối như Story, Assets, 2D engine) và các mod bên trong. Mũi tên đi từ một phụ thuộc tới mod cần nó. Vị trí thẻ và các khối chỉ để đọc: chúng không quyết định thứ tự tải.

Chọn một khối hoặc một mod để xem nó trong **Thanh thuộc tính**. Ở đó bạn có thể đổi phiên bản mà game yêu cầu — **Bất kỳ**, **Tối thiểu**, **Chính xác**, **Nhánh** (`^1` chấp nhận mọi 1.x) và các loại khác. Đây là yêu cầu của game, không phải phiên bản bạn đã cài. **Kiểm tra** liệt kê vấn đề; **Kế hoạch** cho thấy việc áp dụng bộ sưu tập sẽ cài hay bật gì.

![Thanh thuộc tính của một mod có yêu cầu "Exactly 999.0.0" không khớp bản đã cài 1.0.0: yêu cầu và phiên bản đã cài hiện màu đỏ.](assets/version-mismatch.jpg)

Các nút khác của sơ đồ: **+ Cơ chế** và **Thêm mod** thay đổi game, **Nhân bản** tạo bản sao của riêng bạn (và đề nghị sao chép bản lưu), **JSON** cho thấy nội dung gốc, **Lưu** và **Đặt lại** giữ hoặc bỏ thay đổi, **Áp dụng bộ sưu tập** cài và bật những gì kế hoạch liệt kê. Game tích hợp từng bị bạn thay đổi có thể khôi phục bằng **Khôi phục bản tích hợp**; bản lưu của bạn vẫn còn.

Thay đổi với một game được áp dụng vào lần chạy tiếp theo. Bản lưu của game vẫn dùng được khi mod thay đổi: dữ liệu của mod rời khỏi game được giữ riêng và quay lại cùng với mod.

## Khi có trục trặc

1. **Một mod không xuất hiện trong game.** Kiểm tra nó đã bật chưa và bộ sưu tập của game có chứa nó không (mở **Sửa sơ đồ**).
2. **"Cài đặt thất bại".** Kiểm tra kết nối internet và thử lại **Làm mới danh mục**.
3. **Thẻ game báo Thiếu hoặc Vấn đề.** Mở sơ đồ của nó và thẻ **Kiểm tra**: nơi liệt kê mod còn thiếu, khác phiên bản và xung đột.
4. **Xung đột danh mục.** Cùng một mod và phiên bản được hai danh mục phát hành với nội dung khác nhau; hãy tin danh mục mà bạn tin tưởng.
5. **Không có mục tài liệu.** Mod chưa có cẩm nang trong `docs/`; thay vào đó là mô tả trong manifest.
