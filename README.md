# Word Search Book

Word Search Book là ứng dụng Windows dùng để xây dựng workflow tạo sách word search. Repository hiện cung cấp nền tảng kiến trúc, desktop shell WPF + WebView2 và bridge tối thiểu giữa C# với frontend.

## Kiến trúc

```text
src/
├─ WordSearchBook.Core/            # Contract và logic ứng dụng độc lập hạ tầng
├─ WordSearchBook.Infrastructure/  # Implementation và đăng ký dependency injection
└─ WordSearchBook.Desktop/         # WPF host, WebView2 bridge và frontend local
```

Dependency chỉ đi theo hướng `Desktop → Infrastructure → Core`; Desktop cũng được phép dùng trực tiếp contract trong Core.

## Yêu cầu

- Windows với Microsoft Edge WebView2 Runtime.
- .NET SDK 10.0.401.
- Node.js 24.18.0.

## Build và test

```powershell
npm ci --prefix src/WordSearchBook.Desktop/Frontend
npm run verify:css --prefix src/WordSearchBook.Desktop/Frontend
npm test --prefix src/WordSearchBook.Desktop/Frontend
dotnet restore WordSearchBook.sln
dotnet build WordSearchBook.sln --configuration Release --no-restore
dotnet test WordSearchBook.sln --configuration Release --no-build
```

Để chạy ứng dụng:

```powershell
dotnet run --project src/WordSearchBook.Desktop/WordSearchBook.Desktop.csproj
```

## Trạng thái baseline

Frontend gửi message `ping` khi khởi động. Desktop trả `pong` với tên ứng dụng, version và trạng thái `ready`. Word Search Core MVP đã hoạt động độc lập ở backend nhưng chưa được nối vào Desktop/UI.

## Word Search Core MVP

Backend đọc dữ liệu từ `input/{book-name-or-sku}/data.csv`, group theo Topic và yêu cầu mỗi Topic có đúng 20 cặp Keyword/Word Search Key. Global settings nằm tại `settings.json`; brand layout nằm tại `brands/{brand}/settings.json`.

`IWordSearchBookGenerationService` sinh board puzzle/answer, sau đó compose hai trang hoàn chỉnh từ `brands/{brand}/page_layout.png`. Layout bắt buộc là PNG `2588x3375`; thiếu file, sai kích thước hoặc không đọc được sẽ làm generation fail với error code ổn định thay vì dùng fallback.

Topic, Keyword và Word Search Key được chuẩn hóa thành chữ hoa khi đọc CSV. Topic dùng anchor X/Y; page number dùng số thứ tự Topic bắt đầu từ 1; keyword list dùng đúng 20 Keyword hiển thị theo column-major, 5 từ cho mỗi cột trong 4 cột. Mỗi text region hỗ trợ `Left`, `Center`, `Right`; X là anchor theo alignment và Y luôn là cạnh trên. Text vượt trang hoặc chạm keyword khác sẽ fail bằng `page_text_overflow`.

```text
input/{book}/.workspace/cache/{brand}/
├─ manifest.json
└─ topics/
   └─ 001/
      ├─ board-game.png
      ├─ board-game-answer.png
      ├─ page.png
      └─ page-answer.png
```

Core giữ contracts, validation và puzzle engine. Infrastructure chịu trách nhiệm CSV/JSON, System.Drawing và filesystem cache. Desktop gọi workflow này thông qua background task queue.

## Desktop workspace

Desktop dùng sidebar `Books`, `Brands`, `Tasks`, `Settings` và lấy application root cố định từ thư mục chứa executable. Đặt `brands/` và `input/` cạnh ứng dụng; nếu chưa có `settings.json`, ứng dụng tự tạo cấu hình mặc định với board `20x20` và page cố định `2588x3375`. Books cho phép chọn brand, ghi nhớ lựa chọn trong `%LocalAppData%\WordSearchBook\workspace-state.json` và enqueue generation; Brands tạo brand mặc định kèm layout trắng, tìm kiếm, chỉnh anchor/style và validate `page_layout.png` thủ công; Tasks hiển thị queue; Settings lưu cấu hình bằng atomic save.

Mỗi brand lưu certificate tại `brands/{brand}/brand.validation.json`. Workspace startup chỉ so certificate với metadata `page_layout.png`; việc decode PNG và kiểm tra đúng `2588x3375` chỉ chạy qua nút **Validate layout** trong background queue. Generation bị chặn ở cả UI và Core cho đến khi trạng thái layout là `Validated`.

Reader vẫn nhận brand JSON cũ dùng `rectangle` cho Topic, Keyword list và Page number. Migration chỉ diễn ra trong memory; lần Save Brand tiếp theo ghi schema canonical gồm text anchor/alignment và bốn keyword column anchors.

Quy ước kích thước và bố cục Desktop được ghi tại [Desktop UI guidelines](docs/ui-guidelines.md). UI dùng baseline `1600x900`; `MainWindow` có kích thước khởi tạo và tối thiểu `1610x910` để chừa khoảng trống quanh nội dung.

Mọi thao tác đọc/ghi filesystem và generate đều chạy qua một background queue tuần tự. Khi đóng ứng dụng trong lúc task đang chạy, ứng dụng hỏi xác nhận, gửi cancellation và chờ tối đa năm giây.

## License

[MIT](LICENSE)
