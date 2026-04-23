using HultUI;
using TodoListSample;

using var app = new Application("Todo List", 500, 600);
app.SetRoot(new TodoApp().Mount());
app.Run();
