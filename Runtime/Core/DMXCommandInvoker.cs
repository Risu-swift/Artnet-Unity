using System.Collections.Generic;
using System.Linq;

// Command invoker
public class DMXCommandInvoker
{
    private Stack<IDMXCommand> commandHistory = new Stack<IDMXCommand>();
    private int maxHistorySize = 50;
    
    public void ExecuteCommand(IDMXCommand command)
    {
        command.Execute();
        
        commandHistory.Push(command);
        if (commandHistory.Count > maxHistorySize)
        {
            var oldCommands = commandHistory.ToArray();
            commandHistory.Clear();
            for (int i = 0; i < maxHistorySize - 1; i++)
                commandHistory.Push(oldCommands[i]);
        }
    }
    
    public void UndoLastCommand()
    {
        if (commandHistory.Count > 0)
        {
            var command = commandHistory.Pop();
            command.Undo();
        }
    }
    
    public void ClearHistory()
    {
        commandHistory.Clear();
    }
    
    public int CommandCount => commandHistory.Count;
}