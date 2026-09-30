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

Frontend gửi message `ping` khi khởi động. Desktop trả `pong` với tên ứng dụng, version và trạng thái `ready`. Logic domain tạo word-search book sẽ được bổ sung ở các phase sau.

## License

[MIT](LICENSE)
