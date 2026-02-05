using System.IO;
using System.Text;

namespace MooringFitting2026.Services.Logging
{
  /// <summary>
  /// 콘솔 표준 출력(Console.Out)을 가로채서, 
  /// 화면(Console)과 파일(StreamWriter) 양쪽에 동시에 로그를 기록하는 클래스입니다.
  /// </summary>
  public class MultiTextWriter : TextWriter
  {
    private readonly TextWriter _consoleWriter; // 원래의 콘솔 출력
    private readonly TextWriter _fileWriter;    // 파일 기록용 출력

    public MultiTextWriter(TextWriter consoleWriter, TextWriter fileWriter)
    {
      _consoleWriter = consoleWriter;
      _fileWriter = fileWriter;
    }

    // 인코딩은 원래 콘솔의 것을 따름
    public override Encoding Encoding => _consoleWriter.Encoding;

    // 문자 하나를 쓸 때도 양쪽에 씀
    public override void Write(char value)
    {
      _consoleWriter.Write(value);
      _fileWriter.Write(value);
    }

    // 문자열을 쓸 때도 양쪽에 씀
    public override void Write(string value)
    {
      _consoleWriter.Write(value);
      _fileWriter.Write(value);
    }

    // 줄바꿈 시에도 양쪽에 씀
    public override void WriteLine(string value)
    {
      _consoleWriter.WriteLine(value);
      _fileWriter.WriteLine(value);
    }

    // 버퍼를 비울 때도 양쪽 다 수행
    public override void Flush()
    {
      _consoleWriter.Flush();
      _fileWriter.Flush();
    }
  }
}
