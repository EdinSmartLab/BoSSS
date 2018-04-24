var edge = require('electron-edge-js');
var path = require('path');

//Singleton Approach
class BoSSS{
    constructor(){
        if(! this.constructor.prototype.instance ){
            var BoSSS_DLL_path = path.resolve(__dirname, '../src/cs/bin/Release/ElectronWorksheet.dll');
            var requireBoSSS = edge.func({
                assemblyFile: BoSSS_DLL_path,
                typeName: 'BoSSS.Application.BoSSSpad.ElectronInterface',
                methodName: 'Invoke' // This must be Func<object,Task<object>>
            });
            this.BoSSSRuntime = requireBoSSS( null, true);
            this.constructor.prototype.instance = this;
        }
        return this.constructor.prototype.instance;
    }

    runCommand( commandString){
        var that = this;
        var runPromise = new Promise(
            function(resolve, reject){
                that.BoSSSRuntime.runCommand(
                    commandString,
                    async function(error, result) {
                        if (error){
                            reject(error);
                        }
                        resolve(result);
                    }
                );
            }
        );
        return runPromise;
    }

}

const instance = new BoSSS();
Object.freeze(instance);

export default instance;






